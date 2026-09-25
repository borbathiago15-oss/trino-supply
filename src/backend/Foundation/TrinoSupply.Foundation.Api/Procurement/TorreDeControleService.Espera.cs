using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// De quem a linha está esperando, e desde quando.
/// </summary>
/// <param name="Who">Quem tem a bola, com nome quando o sistema o conhece.</param>
/// <param name="Since">Quando a espera começou. Nulo = não dá para datar honestamente.</param>
/// <param name="Days">Dias corridos de espera, ou nulo quando não há <see cref="Since"/>.</param>
/// <param name="Detail">O detalhe que muda a cobrança: o atraso do fornecedor, por exemplo.</param>
public record EsperaDaLinha(string Who, DateTimeOffset? Since, int? Days, string? Detail);

/// <summary>
/// Os aprovadores de um centro de custo, por nível. Os ids acompanham os nomes para a tela
/// saber quando todos os da lista estão impedidos pela segregação de funções.
/// </summary>
public record AlcadasDoCentro(
    IReadOnlyList<string> Nivel1, IReadOnlyList<string> Nivel2,
    IReadOnlyList<Guid>? Ids1 = null, IReadOnlyList<Guid>? Ids2 = null)
{
    public static readonly AlcadasDoCentro Nenhuma = new([], []);
    public IReadOnlyList<string> Do(int nivel) => nivel == ApprovalLevels.Level2 ? Nivel2 : Nivel1;
    public IReadOnlyList<Guid> IdsDo(int nivel) => (nivel == ApprovalLevels.Level2 ? Ids2 : Ids1) ?? [];

    /// <summary>
    /// Quem aprova em cada centro, pela <b>mesma regra que decide</b>: as alçadas por nível
    /// quando cadastradas e, sem ninguém marcado, o vínculo antigo — Nível 1 com o gerente
    /// responsável, Nível 2 com o diretor vinculado a ele. É o que a tela de Centros de Custo
    /// promete ("sem ninguém marcado, o centro segue como hoje"), e é o que
    /// <c>ManagerDecisionAsync</c>/<c>DirectorDecisionAsync</c> aceitam.
    ///
    /// <para>
    /// Ler só o cadastro novo fazia a Torre e o caminho do processo dizerem "sem aprovador
    /// cadastrado" para um centro cujo gerente e diretor estavam lá desde sempre — e o
    /// comprador ia cadastrar de novo o que já existia.
    /// </para>
    /// </summary>
    public static async Task<Dictionary<string, AlcadasDoCentro>> ResolverAsync(
        AppDbContext db, IReadOnlyCollection<string> codigos, CancellationToken ct = default)
    {
        var codes = codigos.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        if (codes.Count == 0) return [];

        var centros = await db.CostCenters.Where(c => codes.Contains(c.Code.ToUpper()))
            .Select(c => new
            {
                c.Code, c.ManagerUserId, c.ManagerName,
                N1 = c.Approvers.Where(a => a.Level == ApprovalLevels.Level1).Select(a => new { a.UserId, a.UserName }).ToList(),
                N2 = c.Approvers.Where(a => a.Level == ApprovalLevels.Level2).Select(a => new { a.UserId, a.UserName }).ToList(),
            })
            .ToListAsync(ct);

        // o diretor de cada gerente, de uma vez — é o Nível 2 do vínculo antigo
        var gerentes = centros.Where(c => c.N2.Count == 0 && c.ManagerUserId is not null)
            .Select(c => c.ManagerUserId!.Value).Distinct().ToList();
        var diretorDoGerente = gerentes.Count == 0
            ? new Dictionary<Guid, (Guid Id, string Nome)>()
            : await db.Users.Where(u => gerentes.Contains(u.Id) && u.DirectorId != null)
                .Join(db.Users, u => u.DirectorId, d => d.Id, (u, d) => new { u.Id, DiretorId = d.Id, Diretor = d.Name })
                .ToDictionaryAsync(x => x.Id, x => (x.DiretorId, x.Diretor), ct);

        return centros.ToDictionary(
            c => c.Code.ToUpperInvariant(),
            c =>
            {
                var gerente = c.ManagerUserId is { } g && !string.IsNullOrWhiteSpace(c.ManagerName)
                    ? (Id: g, Nome: c.ManagerName!) : ((Guid Id, string Nome)?)null;
                var diretor = c.ManagerUserId is { } g2 && diretorDoGerente.TryGetValue(g2, out var d) ? d : ((Guid, string)?)null;
                return new AlcadasDoCentro(
                    c.N1.Count > 0 ? c.N1.Select(a => a.UserName).ToList() : gerente is { } ge ? [ge.Nome] : [],
                    c.N2.Count > 0 ? c.N2.Select(a => a.UserName).ToList() : diretor is { } di ? [di.Item2] : [],
                    c.N1.Count > 0 ? c.N1.Select(a => a.UserId).ToList() : gerente is { } ge2 ? [ge2.Id] : [],
                    c.N2.Count > 0 ? c.N2.Select(a => a.UserId).ToList() : diretor is { } di2 ? [di2.Item1] : []);
            });
    }
}

public partial class TorreDeControleService
{
    /// <summary>
    /// "Aguardando aprovação" e "em cotação" dizem a <b>etapa</b>. Esta é a outra metade da
    /// pergunta, e é a que o comprador de fato faz: <b>de quem</b> está esperando, e <b>há
    /// quanto tempo</b>.
    ///
    /// <para>
    /// Sem ela, a Torre mandava sair da tela para descobrir o óbvio: abrir a cotação para ver
    /// qual fornecedor faltava, abrir o centro de custo para ver quem aprova. Tudo isso o
    /// sistema já sabia — só não estava dizendo.
    /// </para>
    ///
    /// <para>
    /// O <b>desde</b> nunca é chutado. Cada etapa tem a sua marca de entrada — a escolha do
    /// vencedor abre a espera do Nível 1, o Nível 1 abre a do Nível 2 — e onde não há marca,
    /// a data fica nula em vez de virar a data de criação da SC, que contaria como espera
    /// um tempo que a etapa nem existia.
    /// </para>
    /// </summary>
    public static EsperaDaLinha? EsperaDe(
        PurchaseRequisition sc, Quotation? cotacao, PurchaseOrder? pedido,
        AlcadasDoCentro alcadas, DateOnly hoje, DateTimeOffset agora)
    {
        var espera = Quem(sc, cotacao, pedido, alcadas, hoje);
        if (espera is null) return null;
        var (quem, desde, detalhe) = espera.Value;
        var dias = desde is { } d ? Math.Max(0, (int)(agora - d).TotalDays) : (int?)null;
        return new EsperaDaLinha(quem, desde, dias, detalhe);
    }

    private static (string Quem, DateTimeOffset? Desde, string? Detalhe)? Quem(
        PurchaseRequisition sc, Quotation? cotacao, PurchaseOrder? pedido,
        AlcadasDoCentro alcadas, DateOnly hoje)
    {
        // entregue, cancelado, rejeitado: não se espera mais nada de ninguém
        if (pedido is not null)
        {
            if (pedido.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled
                or PurchaseOrderStatus.PartiallyReceived) return null;
            // a nota fiscal separa as duas filas: sem NF quem deve agir é o fornecedor;
            // com NF lançada e material não recebido, a bola é do almoxarifado
            return pedido.Invoices.Count == 0
                ? ($"Faturamento — {pedido.SupplierName}", pedido.CreatedAt, "nota fiscal ainda não lançada")
                : ("Recebimento — almoxarifado", pedido.Invoices.Max(i => i.CreatedAt), "nota lançada, material não recebido");
        }

        if (cotacao is not null)
        {
            switch (cotacao.Status)
            {
                case QuotationStatus.AwaitingManager:
                    return (RotuloDaAlcada(alcadas, ApprovalLevels.Level1), cotacao.SelectedAt, null);
                case QuotationStatus.AwaitingDirector:
                    return (RotuloDaAlcada(alcadas, ApprovalLevels.Level2), cotacao.ManagerApprovedAt, null);
                case QuotationStatus.ApprovedForIssue:
                    return ("Registro da O.C. do ERP — comprador",
                        cotacao.DirectorApprovedAt ?? cotacao.ManagerApprovedAt, null);
                case QuotationStatus.Open or QuotationStatus.Analysis:
                    return DaCotacao(cotacao, hoje);
                // o orçamento está com quem pediu: é dele a decisão de comprar
                case QuotationStatus.BudgetPresented:
                    return ("Decisão de quem pediu — orçamento apresentado", cotacao.SelectedAt, null);
                default:
                    return null;   // rejeitada, cancelada, O.C. emitida
            }
        }

        // Enviada e aprovada esperam a mesma coisa: neste fluxo a SC vai direto para
        // Suprimentos, e a alçada decide uma vez só, com os preços do mapa. Separá-las diria
        // "aguardando aprovação" para uma SC que na verdade aguarda comprador — e a Torre
        // ao lado diz "Atribuir comprador", que é o certo. Duas respostas para a mesma
        // pergunta é o defeito que a regra única existe para evitar.
        return sc.Status switch
        {
            RequisitionStatus.Submitted or RequisitionStatus.Approved when sc.AssignedToId is null =>
                ("Triagem — atribuir comprador", sc.DecidedAt ?? sc.SubmittedAt, null),
            RequisitionStatus.Submitted or RequisitionStatus.Approved =>
                ($"Abertura da cotação — {sc.AssignedToLabel ?? "comprador"}", sc.DecidedAt ?? sc.SubmittedAt, null),
            // acervo do fluxo anterior, em que a SC passava por aprovação antes de cotar
            RequisitionStatus.InApproval =>
                ("Aprovação da solicitação", sc.SubmittedAt, null),
            RequisitionStatus.Returned =>
                ($"Ajuste do solicitante — {sc.RequesterLabel}", sc.DecidedAt, null),
            _ => null,
        };
    }

    /// <summary>
    /// Na cotação a espera é dos convidados que ainda não responderam — <b>com nome</b>. É a
    /// pergunta "quem não entregou?" respondida na própria linha, em vez de exigir abrir o
    /// processo para cruzar convidados com propostas.
    ///
    /// <para>
    /// Respondidos todos, quem deve agir é o comprador: fechar para análise e escolher. Dizer
    /// "aguardando fornecedor" nessa hora mandaria cobrar quem já entregou.
    /// </para>
    /// </summary>
    private static (string, DateTimeOffset?, string?)? DaCotacao(Quotation q, DateOnly hoje)
    {
        var convites = QuotationService.SituacaoDosConvites(q, hoje);
        var faltam = convites.Where(c => c.Pending).ToList();

        if (faltam.Count == 0)
            return q.Suppliers.Count == 0
                ? ("Convite a fornecedores — comprador", q.CreatedAt, "nenhum fornecedor convidado")
                : ("Análise das propostas — comprador", q.CreatedAt, "todas as propostas recebidas");

        var nomes = string.Join(", ", faltam.Take(3).Select(c => c.SupplierName))
                    + (faltam.Count > 3 ? $" e mais {faltam.Count - 3}" : "");
        var atrasados = faltam.Where(c => c.Late).ToList();
        var detalhe = atrasados.Count == 0 ? null
            : atrasados.Count == 1
                ? $"{atrasados[0].SupplierName} está {atrasados[0].DaysLate} dia(s) além do prazo"
                : $"{atrasados.Count} fornecedores além do prazo";

        // a espera começa no convite mais antigo que ainda deve resposta: é dele que o
        // relógio corre, e não do convite mais novo, que reiniciaria a contagem
        var desde = q.Suppliers
            .Where(s => faltam.Any(f => f.SupplierId == s.SupplierId))
            .Select(s => s.InvitedAt).DefaultIfEmpty(q.CreatedAt).Min();

        return ($"Proposta — {nomes}", desde, detalhe);
    }

    /// <summary>
    /// O rótulo da alçada, com os nomes de quem pode decidir.
    ///
    /// <para>
    /// Centro sem aprovador cadastrado <b>diz isso</b>, em vez de mostrar um nível sem dono:
    /// fila parada porque ninguém pode aprovar é exatamente o defeito que precisa aparecer, e
    /// esconder o vazio o deixaria passando por demora normal.
    /// </para>
    /// </summary>
    private static string RotuloDaAlcada(AlcadasDoCentro alcadas, int nivel)
    {
        var nomes = alcadas.Do(nivel);
        return nomes.Count == 0
            ? $"Aprovação Nível {nivel} — sem aprovador cadastrado no centro"
            : $"Aprovação Nível {nivel} — {string.Join(", ", nomes)}";
    }
}
