using TrinoSupply.Foundation.Api.Rotas;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O contrato de `families` na vista do pedido. A entidade guarda um CSV (a busca de pedidos
/// usa ILIKE sobre ele), mas a tela faz `families.join(', ')`: mandar a string crua derrubou
/// `/pedidos` inteira no primeiro pedido com duas famílias, porque texto não tem `join`.
/// </summary>
public class VistaDoPedidoTests
{
    [Fact]
    public void Csv_de_duas_familias_vira_lista_de_duas()
    {
        Assert.Equal(["EPI", "UNIFORME"], Vistas.FamiliasDoPedido("EPI, UNIFORME"));
    }

    [Fact]
    public void Uma_familia_so_continua_sendo_lista()
    {
        // o caso que funcionava por acaso: o servidor só grava o CSV com mais de uma família,
        // e com uma só o campo era nulo — por isso a tela passou tanto tempo de pé
        Assert.Equal(["EPI"], Vistas.FamiliasDoPedido("EPI"));
    }

    [Fact]
    public void Sem_familia_a_lista_e_vazia_e_nunca_nula()
    {
        Assert.Empty(Vistas.FamiliasDoPedido(null));
        Assert.Empty(Vistas.FamiliasDoPedido(""));
        Assert.Empty(Vistas.FamiliasDoPedido("   "));
        Assert.Empty(Vistas.FamiliasDoPedido(" , "));   // separador solto não vira família em branco
    }
}
