import { useState, type FormEvent } from 'react';
import {
  atualizarFormaDePagamento, criarFormaDePagamento, listarFormasDePagamento,
  type FormaDePagamento,
} from '@/api/pagamentos';
import { BadgeAtivo, Campo, Nota } from '@/componentes/formulario';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { useToast } from '@/componentes/Toast';
import { podeComprar, temModulo } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';

/**
 * Cadastro das formas de pagamento — o "como" (boleto, Pix, depósito, cartão,
 * dinheiro). É o que alimenta a lista suspensa do mapa de cotação.
 */
export function FormasDePagamento() {
  const usuario = useUsuario();
  const mantem = podeComprar(usuario) && temModulo(usuario, 'COMPRAS');
  const { avisar } = useToast();
  const [editando, setEditando] = useState<FormaDePagamento | null>(null);
  const [nome, setNome] = useState('');
  const [salvando, setSalvando] = useState(false);

  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarFormasDePagamento(mantem, signal),
    [mantem],
  );

  function editar(f: FormaDePagamento) {
    setEditando(f);
    setNome(f.name);
    rolarPara('form-forma-pagamento');
  }

  function cancelar() {
    setEditando(null);
    setNome('');
  }

  async function alternarSituacao(f: FormaDePagamento) {
    try {
      await atualizarFormaDePagamento(f.id, { active: !f.active });
      avisar(f.active ? 'Forma inativada.' : 'Forma reativada.');
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao alterar a forma.', 'erro'); }
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      if (editando) await atualizarFormaDePagamento(editando.id, { name: nome });
      else await criarFormaDePagamento(nome);
      avisar(editando ? 'Forma atualizada.' : 'Forma cadastrada.');
      cancelar();
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao salvar a forma.', 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <>
      <Painel titulo="Formas de Pagamento">
        <Nota>
          A forma é <b>por onde o dinheiro sai</b>. Cadastrada aqui, ela aparece em lista no
          registro da proposta — e é isso que impede o mesmo boleto de virar “Boleto”,
          “boleto bancario” e “BOLETO” em processos diferentes, sem somar em relatório nenhum.
        </Nota>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !dados.length && <Vazio>Nenhuma forma de pagamento cadastrada ainda.</Vazio>}
        {dados && dados.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-formas-pagamento" className="min-w-[520px]">
              <thead>
                <tr><th>Forma</th><th>Situação</th>{mantem && <th>Ações</th>}</tr>
              </thead>
              <tbody>
                {dados.map((f) => (
                  <tr key={f.id} data-forma={f.name}>
                    <td className="font-semibold">{f.name}</td>
                    <td><BadgeAtivo ativo={f.active} rotuloAtivo="ATIVA" rotuloInativo="INATIVA" /></td>
                    {mantem && (
                      <td className="whitespace-nowrap">
                        <div className="flex gap-1.5">
                          <button type="button" className="botao-secundario !py-1.5" onClick={() => editar(f)}>Editar</button>
                          <button type="button" className={(f.active ? 'botao-perigo' : 'botao-secundario') + ' !py-1.5'}
                            onClick={() => alternarSituacao(f)}>{f.active ? 'Inativar' : 'Reativar'}</button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      {mantem && (
        <Painel id="form-forma-pagamento" titulo={editando ? `Editar forma — ${editando.name}` : 'Nova forma de pagamento'}>
          <form onSubmit={enviar}>
            <Campo id="fp-nome" rotulo="Nome">
              <input id="fp-nome" required minLength={2} placeholder="ex.: Boleto Bancário"
                value={nome} onChange={(e) => setNome(e.target.value)} />
            </Campo>
            <div className="mt-4 flex flex-wrap gap-2">
              <button type="submit" className="botao" disabled={salvando}>
                {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Cadastrar forma'}
              </button>
              {editando && <button type="button" className="botao-secundario" onClick={cancelar}>Cancelar edição</button>}
            </div>
          </form>
          <Nota>
            Nada é apagado: a forma que já foi usada em proposta é <b>inativada</b>, some das listas
            novas e continua legível no histórico. Apagar deixaria a proposta antiga apontando para o nada.
          </Nota>
        </Painel>
      )}
    </>
  );
}
