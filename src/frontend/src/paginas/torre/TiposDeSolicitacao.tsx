import { useState, type FormEvent } from 'react';
import {
  atualizarTipoDeSolicitacao, codigoDoTipo, criarTipoDeSolicitacao, listarTiposDeSolicitacao,
  type TipoDeSolicitacao,
} from '@/api/tiposDeSolicitacao';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { BadgeAtivo, Campo, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);
const VAZIO = { code: '', name: '', description: '' };

/**
 * Cadastro dos tipos de solicitação.
 *
 * <p>
 * O campo já existia na solicitação como <strong>texto livre que nenhuma tela preenchia</strong>.
 * Livre, ele daria "EPI", "epi" e "E.P.I." como três tipos que nunca somam em relatório
 * nenhum — e é sobre esse valor que o prazo por tipo é escolhido, então ele precisa ser uma
 * identidade, não uma digitação.
 * </p>
 */
export function TiposDeSolicitacao() {
  const { avisar } = useToast();
  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarTiposDeSolicitacao(true, signal), []);
  const [form, setForm] = useState(VAZIO);
  const [editando, setEditando] = useState<TipoDeSolicitacao | null>(null);
  const [salvando, setSalvando] = useState(false);

  const pode = dados?.canMaintain ?? false;
  const valido = (editando ? true : codigoDoTipo(form.code).length >= 2) && form.name.trim().length >= 2;

  function editar(t: TipoDeSolicitacao) {
    setEditando(t);
    setForm({ code: t.code, name: t.name, description: t.description ?? '' });
    rolarPara('form-tipo-solicitacao');
  }
  function cancelar() { setEditando(null); setForm(VAZIO); }

  async function alternar(t: TipoDeSolicitacao) {
    try {
      await atualizarTipoDeSolicitacao(t.id, { active: !t.active });
      avisar(t.active ? 'Tipo inativado.' : 'Tipo reativado.');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao alterar o tipo.'), 'erro'); }
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      if (editando) {
        await atualizarTipoDeSolicitacao(editando.id, {
          name: form.name.trim(), description: form.description.trim() || null,
        });
        avisar('Tipo atualizado.');
      } else {
        await criarTipoDeSolicitacao({
          code: codigoDoTipo(form.code), name: form.name.trim(),
          description: form.description.trim() || null,
        });
        avisar('Tipo cadastrado. Defina os prazos dele em Prazos por Etapa.');
      }
      cancelar();
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar o tipo.'), 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <Painel titulo="Tipos de Solicitação">
      <Nota>
        O tipo classifica a solicitação e escolhe <strong>qual conjunto de prazos</strong> vale
        para ela — reposição não corre como emergencial. O <strong>código é a identidade</strong>{' '}
        e fica gravado na SC: por isso ele não muda depois de criado, e o nome, sim. Tipo que
        saiu de uso se <strong>inativa</strong>, nunca se apaga: as solicitações que já o
        escolheram precisam continuar legíveis.
      </Nota>

      {erro && <Erro>{erro}</Erro>}
      {carregando && !dados && <Carregando />}

      {dados && !dados.items.length && (
        <Vazio>Nenhum tipo cadastrado — sem eles, toda SC usa o conjunto de prazos padrão.</Vazio>
      )}

      {dados && dados.items.length > 0 && (
        <div className="overflow-x-auto">
          <table data-testid="tabela-tipos-solicitacao">
            <thead>
              <tr><th>Código</th><th>Nome</th><th>Quando usar</th><th>Situação</th><th></th></tr>
            </thead>
            <tbody>
              {dados.items.map((t) => (
                <tr key={t.id} data-tipo={t.code}>
                  <td className="whitespace-nowrap font-semibold">{t.code}</td>
                  <td>{t.name}</td>
                  <td className="sub min-w-[240px]">{t.description || '—'}</td>
                  <td><BadgeAtivo ativo={t.active} /></td>
                  <td className="whitespace-nowrap">
                    {pode && (
                      <>
                        <button type="button" className="botao-secundario" onClick={() => editar(t)}>
                          Editar
                        </button>{' '}
                        <button type="button" className="botao-secundario" onClick={() => alternar(t)}>
                          {t.active ? 'Inativar' : 'Reativar'}
                        </button>
                      </>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {pode && (
        <form onSubmit={enviar} id="form-tipo-solicitacao" className="mt-3" data-testid="form-tipo-solicitacao">
          <div className="flex flex-wrap items-end gap-2">
            <Campo id="tipo-codigo" rotulo="Código" dica={editando ? '(não muda)' : '(vira caixa alta)'}
              className="min-w-[160px]">
              <input id="tipo-codigo" value={form.code} disabled={!!editando} placeholder="EMERGENCIAL"
                onChange={(e) => setForm((f) => ({ ...f, code: codigoDoTipo(e.target.value) }))} />
            </Campo>
            <Campo id="tipo-nome" rotulo="Nome" className="min-w-[200px] flex-1">
              <input id="tipo-nome" value={form.name} placeholder="Compra emergencial"
                onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} />
            </Campo>
            <Campo id="tipo-descricao" rotulo="Quando usar" dica="(opcional)" className="min-w-[240px] flex-1">
              <input id="tipo-descricao" value={form.description} placeholder="parada de linha, risco de acidente"
                onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))} />
            </Campo>
            <button type="submit" className="botao" disabled={!valido || salvando}>
              {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Cadastrar tipo'}
            </button>
            {editando && (
              <button type="button" className="botao-secundario" onClick={cancelar}>Cancelar</button>
            )}
          </div>
          <Nota>
            A explicação de <strong>quando usar</strong> aparece para quem abre a SC. Sem ela,
            cada um escolhe pelo palpite, e o tipo deixa de significar a mesma coisa entre
            duas pessoas.
          </Nota>
        </form>
      )}
    </Painel>
  );
}
