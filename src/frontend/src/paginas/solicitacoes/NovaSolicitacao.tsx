import { useState, type ChangeEvent, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { familiasDoCatalogo, listarProdutos, type Produto } from '@/api/catalogo';
import { listarCentrosCusto, type CentroCusto } from '@/api/centrosCusto';
import { listarEmpresas, perfilDaEmpresa } from '@/api/empresas';
import { listarLocaisDeEntrega, rotuloDoLocal, type LocalEntrega } from '@/api/locais';
import { listarTiposDeSolicitacao } from '@/api/tiposDeSolicitacao';
import { anexarNaSolicitacao, criarSolicitacao, ROTULO_PRIORIDADE, type ItemNovo, type Prioridade } from '@/api/solicitacoes';
import { Aviso, Painel } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { hojeIso } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

interface LinhaItem { chave: string; produto: string; unidade: string; quantidade: string; familia: string }

/**
 * Opção da lista de famílias que diz "este produto não está no catálogo". Não é uma
 * família: é a ausência dela declarada, e vira DIVERSOS no servidor — a mesma chave que
 * a adjudicação usa por omissão. Existe porque o solicitante às vezes precisa pedir algo
 * que ninguém cadastrou ainda, e obrigá-lo a inventar uma família seria pior do que
 * deixá-lo dizer que não sabe.
 */
export const SEM_CADASTRO = '__SEM_CADASTRO__';

let sequencia = 0;
const novaLinha = (): LinhaItem => ({ chave: 'i' + ++sequencia, produto: '', unidade: '', quantidade: '1', familia: '' });

const VAZIO = {
  justificativa: '', local: '', prioridade: 'NORMAL' as Prioridade, necessidade: '',
  urgenciaMotivo: '', urgenciaImpacto: '', centroCusto: '', empresa: '', observacao: '',
  orcamento: '', tipo: '',
};
type Formulario = typeof VAZIO;

/** O campo do produto aceita escolher do catálogo ("CÓDIGO — descrição") ou descrever. */
export const rotuloDoProduto = (p: Produto) => `${p.code} — ${p.description}`;

/**
 * Itens do catálogo escolhidos nas linhas que são EPI/EPC sem C.A. em nenhum
 * fornecedor. O servidor recusa a SC inteira (IC-ERR-023, NR-06) — a tela avisa
 * na linha para a pendência não aparecer só no envio.
 */
export function itensSemCa(linhas: { produto: string }[], catalogo: Produto[]): Produto[] {
  return linhas
    .map((l) => catalogo.find((p) => rotuloDoProduto(p) === l.produto.trim()))
    .filter((p): p is Produto => !!p && p.compliancePending);
}

/**
 * Linhas do formulário viram itens da API: escolhido do catálogo vira vínculo,
 * digitado à mão vira descrição livre. Linha sem produto é descartada.
 *
 * A família só acompanha o item <b>não cadastrado</b>: com produto do catálogo ela é a
 * do cadastro, e mandá-la daqui abriria a porta para o mesmo produto ficar em duas
 * famílias conforme quem digitou. "Produto não cadastrado" vira nulo — é ausência
 * declarada, e o servidor a resolve como DIVERSOS.
 */
export function itensDoFormulario(linhas: LinhaItem[], catalogo: Produto[]): ItemNovo[] {
  return linhas
    .map((l) => {
      const doCatalogo = catalogo.find((p) => rotuloDoProduto(p) === l.produto.trim());
      const familia = l.familia && l.familia !== SEM_CADASTRO ? l.familia : null;
      return {
        description: doCatalogo ? '' : l.produto.trim(),
        catalogItemId: doCatalogo?.id ?? null,
        unitOfMeasure: l.unidade || null,
        quantity: parseFloat(l.quantidade) || 0,
        family: doCatalogo ? null : familia,
      };
    })
    .filter((i) => i.description || i.catalogItemId);
}

/**
 * A família que a linha mostra. Produto do catálogo exibe a dele, e o campo fica travado:
 * quem escolhe a família de um produto cadastrado é o cadastro, não quem pede.
 */
export function familiaDaLinha(l: LinhaItem, catalogo: Produto[]): { valor: string; travada: boolean } {
  const doCatalogo = catalogo.find((p) => rotuloDoProduto(p) === l.produto.trim());
  return doCatalogo ? { valor: doCatalogo.family ?? '', travada: true } : { valor: l.familia, travada: false };
}

export function NovaSolicitacao() {
  const { avisar } = useToast();
  const navegar = useNavigate();
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [linhas, setLinhas] = useState<LinhaItem[]>([novaLinha()]);
  const [anexos, setAnexos] = useState<File[]>([]);
  const [salvando, setSalvando] = useState(false);

  const { dados } = useCarregar(async (signal) => {
    const empresas = await listarEmpresas(false, signal).catch(() => []);
    // sem CNPJs cadastrados, o nome vem do padrão da O.C.
    const padrao = empresas.length ? null : await perfilDaEmpresa(signal).catch(() => null);
    const nomes = empresas.length ? empresas.map((e) => e.legalName) : [padrao?.legalName].filter(Boolean) as string[];
    if (nomes.length === 1) setForm((f) => (f.empresa ? f : { ...f, empresa: nomes[0] }));
    return {
      catalogo: await listarProdutos(signal).catch(() => [] as Produto[]),
      familias: await familiasDoCatalogo(signal).catch(() => [] as string[]),
      locais: await listarLocaisDeEntrega(signal).catch(() => [] as LocalEntrega[]),
      centros: await listarCentrosCusto(false, signal).catch(() => [] as CentroCusto[]),
      empresas: nomes,
      // o tipo escolhe o conjunto de prazos que a Torre vai cobrar desta SC
      tipos: (await listarTiposDeSolicitacao(false, signal).catch(() => ({ items: [] }))).items,
    };
  }, []);

  const urgente = form.prioridade === 'URGENT';
  const semCa = itensSemCa(linhas, dados?.catalogo ?? []);
  const campo = (k: keyof Formulario) => ({
    value: form[k] as string,
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  function editarLinha(chave: string, campos: Partial<LinhaItem>) {
    setLinhas((ls) => ls.map((l) => (l.chave === chave ? { ...l, ...campos } : l)));
  }
  /** Escolher do catálogo já traz a unidade do produto. */
  function escolherProduto(chave: string, valor: string) {
    const p = (dados?.catalogo ?? []).find((x) => rotuloDoProduto(x) === valor.trim());
    editarLinha(chave, p ? { produto: valor, unidade: p.unitOfMeasure } : { produto: valor });
  }
  function removerLinha(chave: string) {
    if (linhas.length === 1) { avisar('A SC precisa de ao menos um item.', 'erro'); return; }
    setLinhas((ls) => ls.filter((l) => l.chave !== chave));
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    const items = itensDoFormulario(linhas, dados?.catalogo ?? []);
    if (!items.length) { avisar('A SC precisa de ao menos um item.', 'erro'); return; }
    if (semCa.length) {
      avisar(`${semCa[0].description} é ${semCa[0].productTypeLabel ?? 'EPI/EPC'} sem C.A. cadastrado `
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
            const pendente = itensSemCa([l], dados?.catalogo ?? [])[0];
            const familia = familiaDaLinha(l, dados?.catalogo ?? []);
            return (
            <div key={l.chave} className="rounded-lg border border-borda p-3" data-linha-item>
              <Grade2>
                <Campo rotulo="Produto" dica="(escolha do catálogo ou descreva)">
                  <input aria-label="Produto" list="produtos-catalogo" required minLength={3}
                    placeholder="digite para buscar no catálogo ou descreva o item"
                    value={l.produto} onChange={(e) => escolherProduto(l.chave, e.target.value)} />
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
              {/* A família é por item, não por SC: uma solicitação pode misturar EPI e
                  material de escritório, e é a família que diz para qual lote de compra
                  cada linha vai. Produto do catálogo mostra a dele, travada — quem
                  escolhe a família de um produto cadastrado é o cadastro. */}
              <Campo rotulo="Família do produto" className="mt-3"
                dica={familia.travada ? '(do cadastro do produto)' : '(escolha ou marque como não cadastrado)'}>
                <select aria-label={`Família de ${l.produto || 'item ' + l.chave}`}
                  value={familia.valor} disabled={familia.travada}
                  onChange={(e) => editarLinha(l.chave, { familia: e.target.value })}>
                  <option value="">Escolha a família…</option>
                  {familia.travada && familia.valor
                    && !(dados?.familias ?? []).includes(familia.valor)
                    && <option value={familia.valor}>{familia.valor}</option>}
                  {(dados?.familias ?? []).map((f) => <option key={f} value={f}>{f}</option>)}
                  {!familia.travada && <option value={SEM_CADASTRO}>Produto não cadastrado</option>}
                </select>
              </Campo>
              {pendente && (
                <Aviso testid="linha-sem-ca">
                  <strong>{pendente.description}</strong> é {pendente.productTypeLabel ?? 'EPI/EPC'} e está sem C.A.
                  em nenhum fornecedor — informe o C.A. no cadastro antes de solicitar (IC-ERR-023).
                </Aviso>
              )}
            </div>
            );
          })}
        </div>
        <datalist id="produtos-catalogo">
          {(dados?.catalogo ?? []).map((p) => <option key={p.id} value={rotuloDoProduto(p)} />)}
        </datalist>
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
                {(dados?.locais ?? []).map((l) => (
                  <option key={l.id} value={rotuloDoLocal(l)}>{rotuloDoLocal(l)}</option>
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
              <input id="sc-empresa" list="empresas-solicitantes" placeholder="empresa solicitante" {...campo('empresa')} />
              <datalist id="empresas-solicitantes">
                {(dados?.empresas ?? []).map((e) => <option key={e} value={e} />)}
              </datalist>
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
    </Painel>
  );
}
