import { useState, type ChangeEvent, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import type { ProdutoParaEscolha, TamanhoDoProduto } from '@/api/catalogo';
import { listarFamilias } from '@/api/familias';
import { listarCentrosCusto, type CentroCusto } from '@/api/centrosCusto';
import { listarEmpresas, perfilDaEmpresa } from '@/api/empresas';
import { listarLocaisDeEntrega, locaisPorTipo, rotuloDoLocal, type LocalEntrega } from '@/api/locais';
import { listarTiposDeSolicitacao } from '@/api/tiposDeSolicitacao';
import { anexarNaSolicitacao, criarSolicitacao, ROTULO_PRIORIDADE, type ItemNovo, type Prioridade } from '@/api/solicitacoes';
import { Aviso, Badge, Painel } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { hojeIso } from '@/util/formato';
import { quantidade as formatarQuantidade } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { SeletorDeProduto } from './SeletorDeProduto';

/**
 * Uma linha de item da SC. Ela é uma de duas coisas:
 *
 * - **produto do catálogo** (`escolhido`), com quantidade única ou uma quantidade por
 *   tamanho quando o produto tem grade — a bota do 38 ao 44 é uma linha, não sete;
 * - **item fora do catálogo**, descrito à mão, com a família dita por quem pede.
 */
export interface LinhaItem {
  chave: string;
  /** Descrição digitada — só vale para o item fora do catálogo. */
  produto: string;
  unidade: string;
  quantidade: string;
  familia: string;
  escolhido: ProdutoParaEscolha | null;
  /** Quantidade por tamanho, pelo id da variante. Vazio = nenhum tamanho pedido ainda. */
  porTamanho: Record<string, string>;
  /**
   * Item fora do catálogo, pedido **depois** de buscar ("não achou?" no seletor). Sem produto
   * e sem esta marca, a linha ainda não foi preenchida — a busca é a única porta.
   */
  foraDoCatalogo: boolean;
}

/**
 * Opção da lista de famílias que diz "este produto não está no catálogo". Não é uma
 * família: é a ausência dela declarada, e vira DIVERSOS no servidor — a mesma chave que
 * a adjudicação usa por omissão. Existe porque o solicitante às vezes precisa pedir algo
 * que ninguém cadastrou ainda, e obrigá-lo a inventar uma família seria pior do que
 * deixá-lo dizer que não sabe.
 */
export const SEM_CADASTRO = '__SEM_CADASTRO__';

let sequencia = 0;
export const novaLinha = (): LinhaItem => ({
  chave: 'i' + ++sequencia, produto: '', unidade: '', quantidade: '1', familia: '',
  escolhido: null, porTamanho: {}, foraDoCatalogo: false,
});

/** Linhas que ainda não têm produto nem foram marcadas como fora do catálogo. */
export const linhasSemProduto = (linhas: LinhaItem[]) => linhas.filter((l) => !l.escolhido && !l.foraDoCatalogo);

const VAZIO = {
  justificativa: '', local: '', prioridade: 'NORMAL' as Prioridade, necessidade: '',
  urgenciaMotivo: '', urgenciaImpacto: '', centroCusto: '', empresa: '', observacao: '',
  orcamento: '', tipo: '',
};
type Formulario = typeof VAZIO;

/** Quanto se pediu de um tamanho. Campo vazio é zero, e não "um". */
export const quantidadeDoTamanho = (l: LinhaItem, v: TamanhoDoProduto) =>
  parseFloat((l.porTamanho[v.id] ?? '').replace(',', '.')) || 0;

/** Soma da grade — é o número que a linha mostra e o que diz se a linha tem pedido. */
export const totalDaGrade = (l: LinhaItem) =>
  (l.escolhido?.sizes ?? []).reduce((soma, v) => soma + quantidadeDoTamanho(l, v), 0);

/**
 * Tamanhos pedidos que não podem ser solicitados: EPI/EPC sem C.A. em nenhum fornecedor.
 * O servidor recusa a SC inteira (IC-ERR-023, NR-06) — a tela avisa na linha para a
 * pendência não aparecer só no envio.
 */
export function itensSemCa(linhas: LinhaItem[]): { descricao: string; tipo: string; code: string }[] {
  return linhas.flatMap((l) => {
    const p = l.escolhido;
    if (!p) return [];
    const pedidos = p.hasGrade
      ? p.sizes.filter((v) => quantidadeDoTamanho(l, v) > 0)
      : p.sizes.slice(0, 1).filter(() => (parseFloat(l.quantidade) || 0) > 0);
    return pedidos.filter((v) => v.compliancePending).map((v) => ({
      descricao: p.description, tipo: p.productTypeLabel ?? 'EPI/EPC', code: v.code,
    }));
  });
}

/**
 * Linhas do formulário viram itens da API. O produto com grade **expande**: cada tamanho
 * com quantidade vira um item, ligado ao produto daquele tamanho — que é o que a compra
 * precisa, porque a bota 38 e a 39 têm código, preço e C.A. próprios.
 *
 * A família só acompanha o item <b>fora do catálogo</b>: com produto cadastrado ela é a do
 * cadastro, e mandá-la daqui abriria a porta para o mesmo produto ficar em duas famílias
 * conforme quem digitou. "Produto não cadastrado" vira nulo — é ausência declarada, e o
 * servidor a resolve como DIVERSOS.
 */
export function itensDoFormulario(linhas: LinhaItem[]): ItemNovo[] {
  return linhas.flatMap((l): ItemNovo[] => {
    const p = l.escolhido;
    if (p?.hasGrade) {
      return p.sizes
        .map((v) => ({ v, qtd: quantidadeDoTamanho(l, v) }))
        .filter(({ qtd }) => qtd > 0)
        .map(({ v, qtd }) => ({
          description: '', catalogItemId: v.id, unitOfMeasure: p.unitOfMeasure, quantity: qtd, family: null,
        }));
    }
    const qtd = parseFloat(l.quantidade) || 0;
    if (p) {
      return qtd > 0
        ? [{ description: '', catalogItemId: p.sizes[0].id, unitOfMeasure: p.unitOfMeasure, quantity: qtd, family: null }]
        : [];
    }
    const descricao = l.produto.trim();
    if (!descricao) return [];
    return [{
      description: descricao, catalogItemId: null, unitOfMeasure: l.unidade || null, quantity: qtd,
      family: l.familia && l.familia !== SEM_CADASTRO ? l.familia : null,
    }];
  });
}

export function NovaSolicitacao() {
  const { avisar } = useToast();
  const navegar = useNavigate();
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [linhas, setLinhas] = useState<LinhaItem[]>([novaLinha()]);
  const [anexos, setAnexos] = useState<File[]>([]);
  const [salvando, setSalvando] = useState(false);
  /** Chave da linha que está escolhendo produto no catálogo. */
  const [seletor, setSeletor] = useState<string | null>(null);

  const { dados } = useCarregar(async (signal) => {
    const empresas = await listarEmpresas(false, signal).catch(() => []);
    // sem CNPJs cadastrados, o nome vem do padrão da O.C.
    const padrao = empresas.length ? null : await perfilDaEmpresa(signal).catch(() => null);
    const nomes = empresas.length ? empresas.map((e) => e.legalName) : [padrao?.legalName].filter(Boolean) as string[];
    if (nomes.length === 1) setForm((f) => (f.empresa ? f : { ...f, empresa: nomes[0] }));
    return {
      // as famílias ativas do cadastro, e não os nomes que aparecem nos produtos
      familias: (await listarFamilias(false, signal).catch(() => [])).map((f) => f.name),
      locais: await listarLocaisDeEntrega(signal).catch(() => [] as LocalEntrega[]),
      centros: await listarCentrosCusto(false, signal).catch(() => [] as CentroCusto[]),
      empresas: nomes,
      // o tipo escolhe o conjunto de prazos que a Torre vai cobrar desta SC
      tipos: (await listarTiposDeSolicitacao(false, signal).catch(() => ({ items: [] }))).items,
    };
  }, []);

  const urgente = form.prioridade === 'URGENT';
  const semCa = itensSemCa(linhas);
  const campo = (k: keyof Formulario) => ({
    value: form[k] as string,
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  function editarLinha(chave: string, campos: Partial<LinhaItem>) {
    setLinhas((ls) => ls.map((l) => (l.chave === chave ? { ...l, ...campos } : l)));
  }
  /** O produto escolhido traz unidade e família; a grade começa vazia, sem quantidade chutada. */
  function escolherProduto(chave: string, p: ProdutoParaEscolha) {
    editarLinha(chave, {
      escolhido: p, produto: p.description, unidade: p.unitOfMeasure, familia: p.family,
      porTamanho: {}, quantidade: p.hasGrade ? '' : '1', foraDoCatalogo: false,
    });
    setSeletor(null);
  }
  /** "Não achou?" no seletor: o que foi buscado vira o começo da descrição. */
  function descreverForaDoCatalogo(chave: string, termo: string, familia: string) {
    editarLinha(chave, {
      escolhido: null, foraDoCatalogo: true, produto: termo, familia, unidade: '', quantidade: '1', porTamanho: {},
    });
    setSeletor(null);
  }
  const editarTamanho = (l: LinhaItem, v: TamanhoDoProduto, valor: string) =>
    editarLinha(l.chave, { porTamanho: { ...l.porTamanho, [v.id]: valor } });
  function removerLinha(chave: string) {
    if (linhas.length === 1) { avisar('A SC precisa de ao menos um item.', 'erro'); return; }
    setLinhas((ls) => ls.filter((l) => l.chave !== chave));
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    if (linhasSemProduto(linhas).length) {
      avisar('Há item sem produto: use "Buscar no catálogo" na linha, ou exclua a linha vazia.', 'erro');
      return;
    }
    const items = itensDoFormulario(linhas);
    if (!items.length) { avisar('A SC precisa de ao menos um item.', 'erro'); return; }
    if (semCa.length) {
      avisar(`${semCa[0].descricao} (${semCa[0].code}) é ${semCa[0].tipo} sem C.A. cadastrado `
        + '— informe o C.A. no fornecedor antes de solicitar (IC-ERR-023).', 'erro');
      return;
    }
    setSalvando(true);
    try {
      const criada = await criarSolicitacao({
        justification: form.justificativa,
        costCenter: form.centroCusto,
        priority: form.prioridade,
        neededBy: form.necessidade || null,
        items,
        kind: 'AVULSA',
        deliveryLocation: form.local || null,
        needType: form.tipo || null,
        company: form.empresa || null,
        internalNotes: form.observacao || null,
        budget: Number(form.orcamento) > 0 ? Number(form.orcamento) : null,
        urgencyReason: form.urgenciaMotivo || null,
        urgencyImpact: form.urgenciaImpacto || null,
      });
      const { enviados, falhas } = anexos.length
        ? await anexarNaSolicitacao(criada.id, anexos)
        : { enviados: 0, falhas: [] as string[] };
      if (falhas.length) avisar(`Não consegui anexar: ${falhas.join(', ')}.`, 'erro');
      avisar(`SC ${criada.number} criada como rascunho${enviados ? ` com ${enviados} anexo(s)` : ''}. Revise e envie.`);
      navegar('/solicitacoes');
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao criar a SC.', 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <Painel titulo="Inclusão de SC — Solicitação de Compra">
      <form onSubmit={enviar}>
        <h3 className="mb-2 text-[14px] font-bold">Itens</h3>
        <div className="flex flex-col gap-3">
          {linhas.map((l) => {
            const p = l.escolhido;
            const pendente = itensSemCa([l])[0];
            const total = totalDaGrade(l);
            return (
            <div key={l.chave} className="rounded-lg border border-borda p-3" data-linha-item data-produto={p?.baseCode ?? undefined}>
              {/* Uma porta só para o produto: a busca do catálogo. O item fora do catálogo
                  também sai dela ("não achou?"), com o termo buscado como descrição — antes o
                  campo de texto na linha parecia uma segunda busca e deixava o produto
                  cadastrado entrar como texto solto. */}
              {p ? (
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="min-w-0">
                    <div className="font-semibold">{p.description}</div>
                    <div className="sub">
                      {p.baseCode ?? p.sizes[0].code} · {p.family} · {p.unitOfMeasure}
                      {p.productTypeLabel && ` · ${p.productTypeLabel}`}
                    </div>
                  </div>
                  <div className="flex gap-1.5">
                    <button type="button" className="botao-secundario" onClick={() => setSeletor(l.chave)}>Trocar produto</button>
                    <button type="button" className="botao-perigo" onClick={() => removerLinha(l.chave)}>Excluir</button>
                  </div>
                </div>
              ) : l.foraDoCatalogo ? (
                <>
                  <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                    <p className="text-[13px] font-semibold">Item fora do catálogo</p>
                    <button type="button" className="botao-secundario !py-1 text-[12.5px]"
                      onClick={() => setSeletor(l.chave)}>Buscar no catálogo</button>
                  </div>
                  <Grade2>
                    <Campo rotulo="Descrição do item">
                      <input aria-label="Descrição do item" required minLength={3}
                        placeholder="o que você precisa, com medida e especificação"
                        value={l.produto} onChange={(e) => editarLinha(l.chave, { produto: e.target.value })} />
                    </Campo>
                    <Grade2>
                      <Campo rotulo="Unid.">
                        <input aria-label="Unidade" placeholder="UN" value={l.unidade}
                          onChange={(e) => editarLinha(l.chave, { unidade: e.target.value })} />
                      </Campo>
                      <Grade2>
                        <Campo rotulo="Qtde">
                          <input type="number" min="0.01" step="0.01" required aria-label="Quantidade"
                            value={l.quantidade} onChange={(e) => editarLinha(l.chave, { quantidade: e.target.value })} />
                        </Campo>
                        <div className="flex items-end">
                          <button type="button" className="botao-perigo w-full" onClick={() => removerLinha(l.chave)}>Excluir</button>
                        </div>
                      </Grade2>
                    </Grade2>
                  </Grade2>
                </>
              ) : (
                <div className="flex flex-wrap items-center justify-between gap-2" data-testid="linha-sem-produto">
                  <p className="sub">Nenhum produto escolhido ainda.</p>
                  <div className="flex gap-1.5">
                    <button type="button" className="botao" onClick={() => setSeletor(l.chave)}>Buscar no catálogo</button>
                    <button type="button" className="botao-perigo" onClick={() => removerLinha(l.chave)}>Excluir</button>
                  </div>
                </div>
              )}

              {/* A grade: um campo por tamanho, e cada tamanho com quantidade vira um item
                  da SC. É o que evita cadastrar a mesma bota sete vezes para pedir sete
                  numerações — cada tamanho já é um produto, com código e C.A. próprios. */}
              {p?.hasGrade && (
                <div className="mt-3" data-testid="grade-de-tamanhos">
                  <p className="rotulo mb-1.5">Quantidade por tamanho</p>
                  <div className="flex flex-wrap gap-2">
                    {p.sizes.map((v) => (
                      <label key={v.id} data-tamanho={v.size}
                        className={`flex w-[92px] flex-col gap-1 rounded-lg border px-2 py-1.5 ${v.compliancePending ? 'border-perigo/40 bg-perigo-fundo' : 'border-borda'}`}
                        title={v.compliancePending ? `${v.code}: sem C.A. (IC-ERR-023)` : v.code}>
                        <span className="text-[12px] font-semibold">{v.size}</span>
                        <input type="number" min="0" step="1" className="!px-2 !py-1"
                          aria-label={`Tamanho ${v.size} de ${p.description}`}
                          disabled={v.compliancePending} placeholder="0"
                          value={l.porTamanho[v.id] ?? ''} onChange={(e) => editarTamanho(l, v, e.target.value)} />
                      </label>
                    ))}
                  </div>
                  <p className="sub mt-1.5" data-testid="total-da-grade">
                    {total > 0
                      ? `Total: ${formatarQuantidade(total)} ${p.unitOfMeasure} — um item da SC por tamanho pedido.`
                      : 'Informe a quantidade de cada tamanho que você precisa.'}
                  </p>
                </div>
              )}

              {p && !p.hasGrade && (
                <Grade2 className="mt-3">
                  <Campo rotulo="Quantidade">
                    <input type="number" min="0.01" step="0.01" required aria-label={`Quantidade de ${p.description}`}
                      value={l.quantidade} onChange={(e) => editarLinha(l.chave, { quantidade: e.target.value })} />
                  </Campo>
                  <div />
                </Grade2>
              )}

              {/* A família é por item, não por SC: uma solicitação pode misturar EPI e
                  material de escritório, e é a família que diz para qual lote de compra
                  cada linha vai. Produto do catálogo mostra a dele, travada — quem
                  escolhe a família de um produto cadastrado é o cadastro. */}
              {p ? (
                <p className="sub mt-2">Família <Badge classe="bg-slate-100 text-slate-600">{p.family}</Badge> — do cadastro do produto.</p>
              ) : l.foraDoCatalogo && (
                // sem uma segunda lista de família na linha: ela parecia outra busca ao lado da
                // do catálogo. A família do item de fora é a do filtro da própria busca, e
                // "Buscar no catálogo" de novo é o jeito de trocá-la
                <p className="sub mt-2" data-testid="familia-fora-do-catalogo">
                  {l.familia && l.familia !== SEM_CADASTRO
                    ? <>Família <Badge classe="bg-slate-100 text-slate-600">{l.familia}</Badge> — escolhida na busca.</>
                    : 'Sem família: vai como produto não cadastrado. Para classificar, busque de novo escolhendo a família.'}
                </p>
              )}

              {pendente && (
                <Aviso testid="linha-sem-ca">
                  <strong>{pendente.descricao}</strong> ({pendente.code}) é {pendente.tipo} e está sem C.A.
                  em nenhum fornecedor — informe o C.A. no cadastro antes de solicitar (IC-ERR-023).
                </Aviso>
              )}
            </div>
            );
          })}
        </div>
        <button type="button" className="botao-secundario mt-3" onClick={() => setLinhas((ls) => [...ls, novaLinha()])}>
          + Adicionar item
        </button>

        {/*
          O essencial primeiro: o que, por quê, para qual centro, para quando. O resto é
          opcional e fica recolhido — um formulário de doze campos faz o solicitante achar
          que precisa de todos, e é aí que ele desiste ou inventa.
        */}
        <Campo id="sc-justificativa" rotulo="Justificativa da solicitação" className="mt-5">
          <input id="sc-justificativa" required placeholder="Por que esta compra é necessária?" {...campo('justificativa')} />
        </Campo>

        <div className="mt-3 grid gap-3 md:grid-cols-3">
          <Campo id="sc-cc" rotulo="Centro de Custo">
            <select id="sc-cc" required {...campo('centroCusto')}>
              <option value="">Selecione o centro de custo…</option>
              {(dados?.centros ?? []).map((c) => (
                <option key={c.id} value={c.code}>{c.code} — {c.name}{c.region ? ` · ${c.region}` : ''}</option>
              ))}
            </select>
          </Campo>
          {/* a submissão recusa data no passado (PR-ERR-050): o campo diz isso antes */}
          <Campo id="sc-necessidade" rotulo="Precisa até" dica="(de hoje em diante)">
            <input id="sc-necessidade" type="date" min={hojeIso()} {...campo('necessidade')} />
          </Campo>
          <Campo id="sc-prioridade" rotulo="Prioridade">
            <select id="sc-prioridade" {...campo('prioridade')}>
              <option value="NORMAL">{ROTULO_PRIORIDADE.NORMAL}</option>
              <option value="URGENT">{ROTULO_PRIORIDADE.URGENT}</option>
            </select>
          </Campo>
        </div>

        {urgente && (
          <Grade2 className="mt-3" >
            <Campo id="sc-urg-motivo" rotulo="Justificativa da urgência" dica="(obrigatória)">
              <input id="sc-urg-motivo" required placeholder="ex.: parada de linha na obra" {...campo('urgenciaMotivo')} />
            </Campo>
            <Campo id="sc-urg-impacto" rotulo="Impacto se não comprar" dica="(obrigatório)">
              <input id="sc-urg-impacto" required placeholder="ex.: equipe parada e multa contratual" {...campo('urgenciaImpacto')} />
            </Campo>
          </Grade2>
        )}

        <details className="mt-4 rounded-lg border border-borda px-3 py-2" data-testid="mais-detalhes">
          <summary className="cursor-pointer text-[13.5px] font-semibold text-marca">
            Mais detalhes <span className="sub font-normal">(opcional: local de entrega, tipo, empresa, orçamento, observação, anexos)</span>
          </summary>
          <Grade2 className="mt-3">
            <Campo id="sc-local" rotulo="Local de Entrega">
              <select id="sc-local" {...campo('local')}>
                <option value="">Selecione…</option>
                {locaisPorTipo(dados?.locais ?? []).map((g) => (
                  <optgroup key={g.kind} label={g.rotulo}>
                    {g.locais.map((l) => (
                      <option key={l.id} value={rotuloDoLocal(l)}>{rotuloDoLocal(l)}</option>
                    ))}
                  </optgroup>
                ))}
              </select>
            </Campo>
            {/* o tipo decide qual conjunto de prazos a Torre cobra desta SC. Em branco,
                vale o padrão — que é o que valia antes de os tipos existirem */}
            {!!dados?.tipos.length && (
              <Campo id="sc-tipo" rotulo="Tipo da solicitação" dica="(define o prazo de atendimento)">
                <select id="sc-tipo" {...campo('tipo')}>
                  <option value="">Padrão</option>
                  {dados.tipos.map((t) => (
                    <option key={t.code} value={t.code} title={t.description ?? undefined}>
                      {t.name}
                    </option>
                  ))}
                </select>
              </Campo>
            )}
          </Grade2>

          <Grade2 className="mt-3">
            <Campo id="sc-empresa" rotulo="Empresa">
              {/* do cadastro de CNPJs, e não digitada: texto livre dava a mesma empresa escrita
                  de três jeitos, e a Torre e o cockpit, que agrupam por ela, contavam três */}
              <select id="sc-empresa" {...campo('empresa')}>
                <option value="">{dados?.empresas.length ? 'Selecione a empresa…' : 'Nenhuma empresa cadastrada'}</option>
                {(dados?.empresas ?? []).map((e) => <option key={e} value={e}>{e}</option>)}
              </select>
            </Campo>
            {/* §17: o orçamento é a régua do saving que só o solicitante conhece. Opcional
                de propósito — quem não tem número não é obrigado a inventar um */}
            <Campo id="sc-orcamento" rotulo="Orçamento previsto (R$)"
              dica="(opcional — fechar abaixo dele vira saving)">
              <input id="sc-orcamento" type="number" min="0" step="0.01" placeholder="ex.: 1200,00"
                {...campo('orcamento')} />
            </Campo>
          </Grade2>

          <Campo id="sc-observacao" rotulo="Observação Interna" className="mt-3">
            <textarea id="sc-observacao" rows={3} {...campo('observacao')} />
          </Campo>

          <Campo id="sc-anexos" className="mt-3" rotulo="Anexos"
            dica="(PDF, imagem ou planilha — pode escolher mais de um)">
            <input id="sc-anexos" type="file" multiple accept=".pdf,.png,.jpg,.jpeg,.xlsx,.xls,.csv,.docx"
              onChange={(ev: ChangeEvent<HTMLInputElement>) => setAnexos([...(ev.target.files ?? [])])} />
          </Campo>
        </details>

        <button type="submit" className="botao mt-4 w-full" disabled={salvando}>
          {salvando ? 'Criando…' : 'Criar rascunho da SC'}
        </button>
        <Nota>A SC nasce como rascunho: você revisa em “Minhas Solicitações (SC)” e envia quando estiver pronta.</Nota>
      </form>

      {seletor && (
        <SeletorDeProduto familias={dados?.familias ?? []} aoFechar={() => setSeletor(null)}
          aoEscolher={(p) => escolherProduto(seletor, p)}
          aoDescrever={(termo, familia) => descreverForaDoCatalogo(seletor, termo, familia)} />
      )}
    </Painel>
  );
}
