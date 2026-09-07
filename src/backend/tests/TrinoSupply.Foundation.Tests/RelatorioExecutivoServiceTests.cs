using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Relatório executivo de compras — os seis blocos que a diretoria lê sobre um mesmo
/// recorte. O que estes testes protegem não é o número em si, é o que faz o número
/// mentir: contar o saving duas vezes quando a compra foi dividida, achar que o pedido
/// sabe de que empresa é, e somar um total sem dizer o que ficou de fora.
/// </summary>
public class RelatorioExecutivoServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Agora = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Carla = Guid.NewGuid();
    private static readonly Guid Diego = Guid.NewGuid();
    private static readonly FiltroRelatorio Agosto =
        new(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), null, null, null);

    private static (AppDbContext Db, RelatorioExecutivoService Svc) Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new RelatorioExecutivoService(db, new FixedTimeProvider(Agora)));
    }

    private static PurchaseOrder Pedido(string numero, string fornecedor, decimal valor,
        Guid comprador, string compradorLabel, Guid? prId = null, string? familia = null,
        Guid? cotacaoId = null, string? erp = "OC-SENIOR-1", string? semOcMotivo = null,
        Guid? fornecedorId = null, int dia = 10) => new()
        {
            Number = numero,
            SupplierId = fornecedorId ?? Guid.NewGuid(),
            SupplierName = fornecedor,
            IssuedBy = comprador,
            IssuedByLabel = compradorLabel,
            SourcePrId = prId,
            QuotationId = cotacaoId,
            ErpNumber = erp,
            NoErpReason = semOcMotivo,
            TotalValue = valor,
            Status = PurchaseOrderStatus.Issued,
            CreatedAt = new DateTimeOffset(2026, 8, dia, 9, 0, 0, TimeSpan.Zero),
            Items =
            {
                new PurchaseOrderItem
                {
                    Description = $"item de {numero}", Quantity = 10, UnitPrice = valor / 10,
                    Family = familia, UnitOfMeasure = "UN",
                },
            },
        };

    private static PurchaseRequisition Solicitacao(string numero, string centro, string prioridade = "NORMAL",
        string? empresaDigitada = null, string? motivo = null) => new()
        {
            Number = numero, CostCenter = centro, Priority = prioridade, Company = empresaDigitada,
            UrgencyReason = motivo, UrgencyImpact = motivo is null ? null : "para a operação",
            RequesterLabel = "Ana Solicitante", Status = RequisitionStatus.Approved,
            CreatedAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public async Task Relatorio_soma_por_familia_e_o_saving_de_cada_comprador()
    {
        var (db, svc) = Build();
        var cotacao = new Quotation
        {
            Number = "RFQ-2026-000001", SourcePrId = Guid.NewGuid(),
            BaselineValue = 12_000m, NegotiatedValue = 10_000m, SavingValue = 2_000m,
            CreatedAt = Agora,
        };
        db.Quotations.Add(cotacao);
        db.PurchaseOrders.AddRange(
            Pedido("PO-1", "Alfa", 10_000m, Carla, "Carla Compradora", familia: "MATERIAL DE LIMPEZA", cotacaoId: cotacao.Id),
            Pedido("PO-2", "Beta", 4_000m, Diego, "Diego Comprador", familia: "EPI"),
            Pedido("PO-3", "Alfa", 6_000m, Diego, "Diego Comprador", familia: "MATERIAL DE LIMPEZA"));
        await db.SaveChangesAsync();

        var r = await svc.GerarAsync(Agosto);

        Assert.Equal(20_000m, r.Kpis.Spend);
        Assert.Equal(3, r.Kpis.Orders);

        // 1 — o que foi comprado: limpeza soma os dois pedidos e leva 80% do período
        var limpeza = r.Families.Single(f => f.Family == "MATERIAL DE LIMPEZA");
        Assert.Equal(16_000m, limpeza.Value);
        Assert.Equal(80.0, limpeza.Percent);
        Assert.Equal(2, limpeza.Orders);

        // 2 — o saving fica com quem negociou, e a % é contra a primeira proposta
        var carla = r.Buyers.Single(b => b.Buyer == "Carla Compradora");
        Assert.Equal(2_000m, carla.Saving);
        Assert.Equal(12_000m, carla.Baseline);
        Assert.Equal(16.7, carla.SavingPercent);
        Assert.Equal(0m, r.Buyers.Single(b => b.Buyer == "Diego Comprador").Saving);

        // 3 — concentração: Alfa fica com 16 mil dos 20 mil
        Assert.Equal(80.0, r.Suppliers.Top1Percent);
        Assert.Equal(2, r.Suppliers.SupplierCount);
    }

    [Fact]
    public async Task Compra_dividida_em_duas_OCs_nao_conta_o_saving_duas_vezes()
    {
        // A adjudicação por família emite uma O.C. por fornecedor a partir do MESMO
        // processo. Somar o saving pedido a pedido dobraria o ganho do comprador — e é
        // o tipo de erro que ninguém percebe olhando a tela, só a diretoria na reunião.
        var (db, svc) = Build();
        var cotacao = new Quotation
        {
            Number = "RFQ-2026-000002", SourcePrId = Guid.NewGuid(),
            BaselineValue = 20_000m, NegotiatedValue = 15_000m, SavingValue = 5_000m, CreatedAt = Agora,
        };
        db.Quotations.Add(cotacao);
        db.PurchaseOrders.AddRange(
            Pedido("PO-1", "Alfa", 9_000m, Carla, "Carla Compradora", familia: "EPI", cotacaoId: cotacao.Id),
            Pedido("PO-2", "Beta", 6_000m, Carla, "Carla Compradora", familia: "FARDAMENTO", cotacaoId: cotacao.Id));
        await db.SaveChangesAsync();

        var r = await svc.GerarAsync(Agosto);

        var carla = r.Buyers.Single();
        Assert.Equal(5_000m, carla.Saving);       // e não 10.000
        Assert.Equal(1, carla.Processes);         // um processo, dois pedidos
        Assert.Equal(2, carla.Orders);
        Assert.Equal(15_000m, carla.Spend);
        Assert.Equal(5_000m, r.Kpis.SavingTotal);
    }

    [Fact]
    public async Task Corte_por_empresa_vem_do_centro_de_custo_da_solicitacao()
    {
        // O pedido não guarda empresa nem centro de custo: os dois vêm da SC de origem,
        // e a empresa sai do cadastro do CC — o mesmo caminho do cabeçalho do PDF da O.C.
        var (db, svc) = Build();
        var trinoNe = new Company { LegalName = "Trino Nordeste LTDA", TaxId = "11111111000191" };
        var trinoSp = new Company { LegalName = "Trino São Paulo LTDA", TaxId = "22222222000192" };
        db.Companies.AddRange(trinoNe, trinoSp);
        db.CostCenters.AddRange(
            new CostCenter { Code = "CC-NE-01", Name = "Filial Recife", CompanyId = trinoNe.Id },
            new CostCenter { Code = "CC-SP-01", Name = "Filial Campinas", CompanyId = trinoSp.Id });
        var scNe = Solicitacao("PR-2026-000001", "CC-NE-01");
        var scSp = Solicitacao("PR-2026-000002", "CC-SP-01");
        db.Requisitions.AddRange(scNe, scSp);
        db.PurchaseOrders.AddRange(
            Pedido("PO-1", "Alfa", 7_000m, Carla, "Carla Compradora", prId: scNe.Id, familia: "EPI"),
            Pedido("PO-2", "Beta", 3_000m, Carla, "Carla Compradora", prId: scSp.Id, familia: "EPI"));
        await db.SaveChangesAsync();

        var todos = await svc.GerarAsync(Agosto);
        Assert.Equal(10_000m, todos.Kpis.Spend);
        Assert.Contains("Trino Nordeste LTDA", todos.FilterOptions.Companies);

        var nordeste = await svc.GerarAsync(Agosto with { Company = "Trino Nordeste LTDA" });
        Assert.Equal(7_000m, nordeste.Kpis.Spend);
        Assert.Equal("Alfa", Assert.Single(nordeste.Suppliers.Rows).Supplier);

        var recife = await svc.GerarAsync(Agosto with { CostCenter = "CC-NE-01" });
        Assert.Equal(7_000m, recife.Kpis.Spend);
        Assert.Equal("CC-NE-01 — Filial Recife", recife.CostCenterLabel);
    }

    [Fact]
    public async Task Pedido_sem_solicitacao_de_origem_fica_fora_do_corte_e_o_relatorio_diz()
    {
        // O pedido criado direto não tem SC, logo não tem empresa nem centro de custo.
        // Filtrar por empresa o deixa de fora — e um total que não fecha com o spend do
        // período precisa dizer por quê, senão a diretoria soma errado sem saber.
        var (db, svc) = Build();
        var empresa = new Company { LegalName = "Trino Nordeste LTDA", TaxId = "11111111000191" };
        db.Companies.Add(empresa);
        db.CostCenters.Add(new CostCenter { Code = "CC-NE-01", Name = "Filial Recife", CompanyId = empresa.Id });
        var sc = Solicitacao("PR-2026-000001", "CC-NE-01");
        db.Requisitions.Add(sc);
        db.PurchaseOrders.AddRange(
            Pedido("PO-1", "Alfa", 7_000m, Carla, "Carla Compradora", prId: sc.Id, familia: "EPI"),
            Pedido("PO-2", "Beta", 3_000m, Carla, "Carla Compradora", familia: "EPI"));   // sem SC
        await db.SaveChangesAsync();

        var todos = await svc.GerarAsync(Agosto);
        Assert.Equal(10_000m, todos.Kpis.Spend);
        Assert.Equal(1, todos.Coverage.OrdersWithoutPr);
        Assert.Equal(3_000m, todos.Coverage.ValueWithoutPr);
        Assert.False(todos.Coverage.Capped);

        var nordeste = await svc.GerarAsync(Agosto with { Company = "Trino Nordeste LTDA" });
        Assert.Equal(7_000m, nordeste.Kpis.Spend);
        Assert.Equal(0, nordeste.Coverage.OrdersWithoutPr);   // o de fora já não está no recorte
    }

    [Fact]
    public async Task Compras_sem_OC_do_ERP_saem_com_a_justificativa_de_cada_uma()
    {
        // PO-BR-011: sem O.C. no SENIOR a compra não fecha, salvo a observação que diz
        // por quê. O relatório transforma a exceção em número — e não confunde a exceção
        // com o pedido que só ainda não registrou a O.C.
        var (db, svc) = Build();
        var pendente = Pedido("PO-2026-000004", "Delta", 4_000m, Carla, "Carla Compradora", erp: null);
        var anomalia = Pedido("PO-2026-000003", "Gama", 2_000m, Carla, "Carla Compradora", erp: null);
        anomalia.Status = PurchaseOrderStatus.Received;   // andou sem O.C. e sem justificativa
        db.PurchaseOrders.AddRange(
            Pedido("PO-2026-000001", "Alfa", 5_000m, Carla, "Carla Compradora", erp: "OC-SENIOR-77"),
            Pedido("PO-2026-000002", "Beta", 3_000m, Carla, "Carla Compradora",
                erp: null, semOcMotivo: "compra emergencial fora do horário do ERP"),
            anomalia, pendente);
        await db.SaveChangesAsync();

        var r = await svc.GerarAsync(Agosto);

        // o bloco é a exceção: uma compra, 3 mil de 14 mil
        Assert.Equal(1, r.WithoutErp.Orders);
        Assert.Equal(3_000m, r.WithoutErp.Value);
        Assert.Equal(21.4, r.WithoutErp.Percent);
        Assert.Equal("compra emergencial fora do horário do ERP",
            r.WithoutErp.Items.Single(i => i.Number == "PO-2026-000002").Reason);

        // o pedido em aberto é fila, não violação: conta à parte e não vai para a lista
        Assert.Equal(1, r.WithoutErp.PendingOrders);
        Assert.Equal(4_000m, r.WithoutErp.PendingValue);
        Assert.DoesNotContain(r.WithoutErp.Items, i => i.Number == "PO-2026-000004");

        // o que andou sem O.C. e sem justificativa é anomalia — aparece destacado
        Assert.Equal(1, r.WithoutErp.ClosedWithoutReason);
        Assert.Null(r.WithoutErp.Items.Single(i => i.Number == "PO-2026-000003").Reason);

        // o número do pedido é a própria numeração PO-ano-sequência: o sistema não inventa O.C.
        Assert.All(r.WithoutErp.Items, i => Assert.StartsWith("PO-2026-", i.Number));
    }

    [Fact]
    public async Task Peso_das_urgentes_e_OTIF_por_fornecedor_saem_do_mesmo_recorte()
    {
        var (db, svc) = Build();
        var urgente = Solicitacao("PR-2026-000001", "CC-NE-01", "URGENT", motivo: "parada de linha");
        var normal = Solicitacao("PR-2026-000002", "CC-NE-01");
        db.Requisitions.AddRange(urgente, normal);

        var atrasado = Pedido("PO-1", "Alfa", 6_000m, Carla, "Carla Compradora", prId: urgente.Id, familia: "EPI");
        atrasado.PromisedDate = new DateOnly(2026, 8, 15);
        atrasado.DeliveryCompletedAt = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
        atrasado.Items[0].ReceivedQuantity = 10;

        var noPrazo = Pedido("PO-2", "Beta", 2_000m, Carla, "Carla Compradora", prId: normal.Id, familia: "EPI");
        noPrazo.PromisedDate = new DateOnly(2026, 8, 20);
        noPrazo.DeliveryCompletedAt = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
        noPrazo.Items[0].ReceivedQuantity = 10;

        // entrega ainda aberta: não conta nem a favor nem contra
        var emAberto = Pedido("PO-3", "Beta", 2_000m, Carla, "Carla Compradora", prId: normal.Id, familia: "EPI");
        emAberto.PromisedDate = new DateOnly(2026, 9, 30);

        db.PurchaseOrders.AddRange(atrasado, noPrazo, emAberto);
        await db.SaveChangesAsync();

        var r = await svc.GerarAsync(Agosto);

        // 4 — urgência: 6 mil de 10 mil
        Assert.Equal(1, r.Urgent.Orders);
        Assert.Equal(60.0, r.Urgent.Percent);
        var caso = Assert.Single(r.Urgent.Items);
        Assert.Equal("PR-2026-000001", caso.PrNumber);
        Assert.Equal("parada de linha", caso.Reason);

        // 5 — OTIF: duas entregas medidas, uma no prazo
        Assert.Equal(50.0, r.Kpis.OtifPercent);
        Assert.Equal(0.0, r.Otif.Single(o => o.Supplier == "Alfa").OtifPercent);
        var beta = r.Otif.Single(o => o.Supplier == "Beta");
        Assert.Equal(1, beta.Measured);           // a entrega em aberto ficou de fora
        Assert.Equal(100.0, beta.OtifPercent);
    }

    [Fact]
    public async Task Pedido_cancelado_e_pedido_de_outro_mes_ficam_fora_do_recorte()
    {
        var (db, svc) = Build();
        var cancelado = Pedido("PO-1", "Alfa", 9_000m, Carla, "Carla Compradora", familia: "EPI");
        cancelado.Status = PurchaseOrderStatus.Cancelled;
        var julho = Pedido("PO-2", "Beta", 8_000m, Carla, "Carla Compradora", familia: "EPI");
        julho.CreatedAt = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
        db.PurchaseOrders.AddRange(cancelado, julho,
            Pedido("PO-3", "Gama", 1_000m, Carla, "Carla Compradora", familia: "EPI"));
        await db.SaveChangesAsync();

        var r = await svc.GerarAsync(Agosto);

        Assert.Equal(1_000m, r.Kpis.Spend);
        Assert.Equal("Gama", Assert.Single(r.Suppliers.Rows).Supplier);
    }

    [Fact]
    public async Task Familia_ausente_no_item_cai_no_produto_de_catalogo()
    {
        var (db, svc) = Build();
        var produto = new CatalogItem
        {
            Code = "LMP-001", Description = "Detergente neutro", Family = "MATERIAL DE LIMPEZA",
            UnitOfMeasure = "UN", CreatedAt = Agora, UpdatedAt = Agora,
        };
        db.CatalogItems.Add(produto);
        var pedido = Pedido("PO-1", "Alfa", 1_000m, Carla, "Carla Compradora");
        pedido.Items[0].Family = null;
        pedido.Items[0].CatalogItemId = produto.Id;
        var semNada = Pedido("PO-2", "Beta", 500m, Carla, "Carla Compradora");
        semNada.Items[0].Family = null;
        db.PurchaseOrders.AddRange(pedido, semNada);
        await db.SaveChangesAsync();

        var r = await svc.GerarAsync(Agosto);

        Assert.Equal(1_000m, r.Families.Single(f => f.Family == "MATERIAL DE LIMPEZA").Value);
        Assert.Equal(500m, r.Families.Single(f => f.Family == "SEM FAMÍLIA").Value);
    }

    [Fact]
    public async Task O_PDF_sai_com_o_recorte_no_cabecalho()
    {
        var (db, svc) = Build();
        var sc = Solicitacao("PR-2026-000001", "CC-NE-01", "URGENT", motivo: "parada de linha");
        db.Requisitions.Add(sc);
        db.PurchaseOrders.Add(Pedido("PO-1", "Alfa", 5_000m, Carla, "Carla Compradora",
            prId: sc.Id, familia: "EPI", erp: null, semOcMotivo: "ERP indisponível na emissão"));
        await db.SaveChangesAsync();

        var r = await svc.GerarAsync(Agosto);
        var pdf = RelatorioExecutivoPdf.Generate(r, new CompanyProfile { LegalName = "Trino Nordeste LTDA" },
            "Carla Compradora");

        Assert.True(pdf.Length > 1_000);
        Assert.Equal("%PDF"u8.ToArray(), pdf.Take(4).ToArray());
    }
}
