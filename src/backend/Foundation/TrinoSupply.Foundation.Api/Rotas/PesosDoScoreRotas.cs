using System.Security.Claims;
using TrinoSupply.Foundation.Api.Procurement;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// A régua do score multicritério: quanto vale preço, entrega, pagamento, OTIF e risco na
/// comparação das propostas.
///
/// <para>
/// <b>Ler é de todo mundo, alterar é do administrador.</b> Quem vê o score na cotação precisa
/// saber de que ele é feito — esconder a régua de quem lê o número transformaria a explicação
/// em privilégio. Mudar a régua, ao contrário, muda a ordem de todas as comparações da
/// empresa de uma vez, e isso não é decisão de tela de comprador.
/// </para>
/// </summary>
public static class PesosDoScoreRotas
{
    public static void MapPesosDoScore(this WebApplication app)
    {
        static object View(ScoreWeights w) => new
        {
            price = w.Price, delivery = w.Delivery, payment = w.Payment,
            otif = w.Otif, risk = w.Risk,
            updatedAt = w.UpdatedAt, updatedByLabel = w.UpdatedByLabel,
            // os rótulos e as explicações vêm com os pesos: a tela de configuração é a mesma
            // que a de leitura do score, e nenhuma das duas inventa texto de critério
            criteria = w.Criterios().Select(c => new
            {
                code = c.Code, label = c.Label,
                weightPct = Math.Round(c.Weight * 100, 1), help = c.Help,
            }),
        };

        var pesos = app.MapGroup("/api/v1/score-weights").RequireAuthorization();
        pesos.AddEndpointFilter(RejectSupplierRole());

        pesos.MapGet("/", async (ScoreWeightsService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var atuais = await svc.AtuaisAsync();
            return Ok(new
            {
                weights = View(atuais),
                canEdit = ScoreWeightsService.CanEdit(RoleOf(p)),
            }, ctx);
        });

        pesos.MapPut("/", async (PesosInput body, ScoreWeightsService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = BuildActor(p);
            if (actor is null) return Error(ctx, 403, "SCR-ERR-900", "Seu papel não altera os pesos do score.");
            var (salvos, error) = await svc.SalvarAsync(actor, body);
            return error is not null
                ? Error(ctx, error.Code == "SCR-ERR-900" ? 403 : 400, error.Code, error.Message)
                : Ok(View(salvos!), ctx);
        });
    }
}
