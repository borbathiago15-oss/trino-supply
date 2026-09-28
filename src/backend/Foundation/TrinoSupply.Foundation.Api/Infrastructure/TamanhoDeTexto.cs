using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Infrastructure;

/// <summary>
/// O tamanho dos textos, conferido <b>antes</b> de gravar (auditoria A6). O limite existia só
/// na coluna: o texto longo demais chegava ao Postgres e voltava como 500, sem dizer ao usuário
/// o que encurtar. A régua é a do próprio modelo (<c>HasMaxLength</c>) — repetir os números
/// no serviço daria dois donos para o mesmo limite.
/// </summary>
public static class TamanhoDeTexto
{
    /// <summary>
    /// O primeiro campo de <paramref name="entidade"/> que passa do limite, com o rótulo que a
    /// pessoa reconhece; nulo quando tudo cabe.
    /// </summary>
    public static UserError? Conferir(DbContext db, object entidade, string codigo,
        IReadOnlyDictionary<string, string>? rotulos = null)
    {
        var tipo = db.Model.FindEntityType(entidade.GetType());
        if (tipo is null) return null;
        foreach (var prop in tipo.GetProperties())
        {
            if (prop.ClrType != typeof(string) || prop.GetMaxLength() is not { } max || prop.PropertyInfo is null) continue;
            if (prop.PropertyInfo.GetValue(entidade) is string valor && valor.Length > max)
            {
                var rotulo = rotulos is not null && rotulos.TryGetValue(prop.Name, out var r) ? r : prop.Name;
                return new(codigo, $"{rotulo} passa de {max:N0} caracteres ({valor.Length:N0} enviados). Encurte o texto.");
            }
        }
        return null;
    }

    /// <summary>
    /// A rede de segurança do <c>SaveChanges</c>: o serviço que ainda não confere o seu texto
    /// recebe um 400 com o campo, e não o 500 do banco.
    /// </summary>
    public static void GarantirNoRastreador(ChangeTracker rastreador)
    {
        foreach (var e in rastreador.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified)) continue;
            foreach (var p in e.Properties)
            {
                if (p.Metadata.ClrType != typeof(string) || p.Metadata.GetMaxLength() is not { } max) continue;
                if (p.CurrentValue is string valor && valor.Length > max)
                    throw new TextoAcimaDoLimiteException(
                        $"O campo {p.Metadata.Name} passa de {max:N0} caracteres ({valor.Length:N0} enviados). Encurte o texto.");
            }
        }
    }
}

public class TextoAcimaDoLimiteException(string message) : Exception(message);
