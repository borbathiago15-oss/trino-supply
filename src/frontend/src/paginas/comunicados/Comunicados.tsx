import { useState } from 'react';
import {
  atualizarComunicado, criarComunicado, enviarImagemDoComunicado, excluirComunicado,
  listarComunicados, type Comunicado,
} from '@/api/comunicados';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data, hojeIso } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

interface Formulario { title: string; body: string; startsOn: string; endsOn: string; active: boolean }

const VAZIO: Formulario = { title: '', body: '', startsOn: hojeIso(), endsOn: hojeIso(), active: true };

/** Situação do comunicado hoje, para a lista não obrigar a comparar datas de cabeça. */
export function situacaoDoComunicado(c: Comunicado, hoje = hojeIso()) {
  if (!c.active) return { rotulo: 'DESLIGADO', classe: 'bg-slate-100 text-slate-500' };
  if (hoje < c.startsOn) return { rotulo: 'AGENDADO', classe: 'bg-aviso-fundo text-aviso' };
  if (hoje > c.endsOn) return { rotulo: 'ENCERRADO', classe: 'bg-slate-100 text-slate-500' };
  return { rotulo: 'NO AR', classe: 'bg-ok-fundo text-ok' };
}

/**
 * Comunicados (administrador): escrever o recado que todo mundo vê ao abrir o
 * sistema, com vigência e, se quiser, um cartaz.
 *
 * A vigência é obrigatória de propósito — comunicado sem data de fim vira
 * moldura da tela e para de ser lido.
 */
export function Comunicados() {
  const { avisar } = useToast();
  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarComunicados(signal), []);
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [editando, setEditando] = useState<Comunicado | null>(null);
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [salvando, setSalvando] = useState(false);

  const campo = (k: keyof Formulario) => ({
    value: String(form[k]),
    onChange: (e: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: e.target.value })),
  });

  function editar(c: Comunicado) {
    setEditando(c);
    setForm({
      title: c.title, body: c.body ?? '', startsOn: c.startsOn, endsOn: c.endsOn, active: c.active,
    });
    setArquivo(null);
  }

  function limpar() { setEditando(null); setForm(VAZIO); setArquivo(null); }

  async function salvar() {
    setSalvando(true);
    try {
      const dadosDoForm = {
        title: form.title, body: form.body || null,
        startsOn: form.startsOn, endsOn: form.endsOn, active: form.active,
      };
      const salvo = editando
        ? await atualizarComunicado(editando.id, dadosDoForm)
        : await criarComunicado(dadosDoForm);
      // a imagem sobe depois de existir o comunicado: ela precisa do id para se prender
      if (arquivo) await enviarImagemDoComunicado(salvo.id, arquivo);
      avisar(editando ? 'Comunicado atualizado.' : 'Comunicado publicado.');
      limpar();
      recarregar();
    } catch (e) {
      avisar(mensagem(e, 'Falha ao salvar o comunicado.'), 'erro');
    } finally { setSalvando(false); }
  }

  async function excluir(c: Comunicado) {
    if (!globalThis.confirm(`Excluir o comunicado "${c.title}"? O histórico de quem leu vai junto.`)) return;
    try {
      await excluirComunicado(c.id);
      avisar('Comunicado excluído.');
      if (editando?.id === c.id) limpar();
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao excluir.'), 'erro'); }
  }

  async function alternarAtivo(c: Comunicado) {
    try {
      await atualizarComunicado(c.id, { active: !c.active });
      avisar(c.active ? 'Comunicado tirado do ar.' : 'Comunicado no ar de novo.');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao alterar.'), 'erro'); }
  }

  const podeSalvar = form.title.trim().length >= 3 && !!form.startsOn && !!form.endsOn
    && form.endsOn >= form.startsOn;

  return (
    <>
      <Painel titulo={editando ? `Editando: ${editando.title}` : 'Novo comunicado'}>
        <Grade2>
          <Campo id="com-titulo" rotulo="Título" className="md:col-span-2">
            <input id="com-titulo" maxLength={200} placeholder="o assunto, em uma linha" {...campo('title')} />
          </Campo>
          <Campo id="com-de" rotulo="Vigente de">
            <input id="com-de" type="date" {...campo('startsOn')} />
          </Campo>
          <Campo id="com-ate" rotulo="Até">
            <input id="com-ate" type="date" {...campo('endsOn')} />
          </Campo>
        </Grade2>

        <Campo id="com-texto" rotulo="Mensagem" dica="(opcional — o comunicado pode ser só a imagem)" className="mt-3">
          <textarea id="com-texto" rows={4} maxLength={8000} {...campo('body')} />
        </Campo>

        <Campo id="com-imagem" rotulo="Imagem" dica="(opcional — PNG, JPG ou WEBP, até 10 MB)" className="mt-3">
          <input id="com-imagem" type="file" accept="image/png,image/jpeg,image/webp"
            onChange={(e) => setArquivo(e.target.files?.[0] ?? null)} />
        </Campo>
        {editando?.imageFileName && !arquivo && (
          <Nota>Imagem atual: {editando.imageFileName}. Escolher outra substitui.</Nota>
        )}

        <label className="mt-3 flex items-center gap-2 text-[13.5px]">
          <input type="checkbox" checked={form.active}
            onChange={(e) => setForm((f) => ({ ...f, active: e.target.checked }))} />
          No ar (desmarcado, fica guardado sem aparecer para ninguém)
        </label>

        {!podeSalvar && form.endsOn < form.startsOn && (
          <Nota>A data final da vigência não pode ser anterior à inicial.</Nota>
        )}

        <div className="mt-3 flex flex-wrap gap-2">
          <button type="button" className="botao" disabled={!podeSalvar || salvando} onClick={salvar}>
            {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Publicar comunicado'}
          </button>
          {editando && <button type="button" className="botao-secundario" onClick={limpar}>Cancelar edição</button>}
        </div>
      </Painel>

      <Painel titulo="Comunicados">
        <p className="sub mb-3">
          Quem abre o sistema vê o que estiver <strong>no ar</strong> e ainda não tiver fechado.
          Fechar é por pessoa e fica gravado — não volta quando ela entra de outro computador.
        </p>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando texto="Carregando os comunicados…" />}
        {dados && !dados.length && <Vazio>Nenhum comunicado publicado ainda.</Vazio>}
        {!!dados?.length && (
          <div className="overflow-x-auto">
            <table data-testid="tabela-comunicados" className="min-w-[820px]">
              <thead>
                <tr>
                  <th>Comunicado</th><th>Vigência</th><th>Situação</th>
                  <th>Fecharam</th><th>Publicado por</th><th></th>
                </tr>
              </thead>
              <tbody>
                {dados.map((c) => {
                  const s = situacaoDoComunicado(c);
                  return (
                    <tr key={c.id}>
                      <td className="min-w-[240px]">
                        {c.title}
                        {c.imageFileName && <div className="sub">🖼 {c.imageFileName}</div>}
                      </td>
                      <td className="whitespace-nowrap">{data(c.startsOn)} a {data(c.endsOn)}</td>
                      <td><Badge classe={s.classe}>{s.rotulo}</Badge></td>
                      <td>{c.dismissedCount}</td>
                      <td className="whitespace-nowrap">{c.createdByLabel}</td>
                      <td className="whitespace-nowrap">
                        <button type="button" className="botao-secundario" onClick={() => editar(c)}>Editar</button>{' '}
                        <button type="button" className="botao-secundario" onClick={() => alternarAtivo(c)}>
                          {c.active ? 'Tirar do ar' : 'Pôr no ar'}
                        </button>{' '}
                        <button type="button" className="botao-secundario" onClick={() => excluir(c)}>Excluir</button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Painel>
    </>
  );
}
