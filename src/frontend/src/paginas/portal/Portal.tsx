import { useEffect, useState, type FormEvent } from 'react';
import {
  anexarNaProposta, entrarNoPortal, enviarProposta, EVENTO_PORTAL_EXPIRADO, lerCotacao,
  minhasCotacoes, montarPropostaDoPortal, sairDoPortal, sessaoPortal,
  type CotacaoDoPortal, type Fornecedor,
} from '@/api/portal';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data, dataHora, moeda, quantidade } from '@/util/formato';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

const CAMPOS_VAZIOS = { prazoEntrega: '', condicaoPagamento: '', frete: '', validade: '', observacao: '' };

/** Barra do topo: a marca e, com sessão, quem está logado. */
function Topo({ fornecedor, aoSair }: { fornecedor: Fornecedor | null; aoSair: () => void }) {
  return (
    <header className="flex flex-wrap items-center justify-between gap-3 bg-fundo px-5 py-3.5 text-slate-300">
      <div className="flex items-center gap-3">
        <img src="/assets/brand/trino-supply-mark.png" width={420} height={108} alt="Trino Supply"
          className="h-auto w-[158px] max-w-full" />
        <span className="text-[14px] font-semibold">Portal do Fornecedor</span>
      </div>
      {fornecedor && (
        <div className="flex items-center gap-3 text-[13px]">
          <span>{fornecedor.name}</span>
          <button type="button" className="botao-perigo" onClick={aoSair}>Sair</button>
        </div>
      )}
    </header>
  );
}

function Login({ aoEntrar }: { aoEntrar: (f: Fornecedor) => void }) {
  const { avisar } = useToast();
  const [cnpj, setCnpj] = useState('');
  const [chave, setChave] = useState('');
  const [entrando, setEntrando] = useState(false);

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setEntrando(true);
    try {
      aoEntrar(await entrarNoPortal(cnpj, chave));
    } catch (e) { avisar(mensagem(e, 'Não foi possível entrar.'), 'erro'); }
    finally { setEntrando(false); }
  }

  return (
    <Painel titulo="Acesso do Fornecedor">
      <Nota>
        Informe o CNPJ/CPF do seu cadastro e a chave de acesso fornecida pelo time de Suprimentos.
      </Nota>
      <form className="mt-3" onSubmit={enviar}>
        <Campo id="portal-cnpj" rotulo="CNPJ / CPF">
          <input id="portal-cnpj" required placeholder="somente números" autoComplete="username"
            value={cnpj} onChange={(e) => setCnpj(e.target.value)} />
        </Campo>
        <Campo id="portal-chave" rotulo="Chave de acesso" className="mt-3">
          <input id="portal-chave" required type="password" autoComplete="current-password"
            value={chave} onChange={(e) => setChave(e.target.value)} />
        </Campo>
        <button type="submit" className="botao mt-4 w-full" disabled={entrando}>
          {entrando ? 'Entrando…' : 'Entrar no portal'}
        </button>
      </form>
    </Painel>
  );
}

function Lista({ aoAbrir }: { aoAbrir: (c: CotacaoDoPortal) => void }) {
  const { avisar } = useToast();
  const [busca, setBusca] = useState('');
  const [aplicada, setAplicada] = useState('');
  const [cotacoes, setCotacoes] = useState<CotacaoDoPortal[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    let vivo = true;
    const ctl = new AbortController();
    setErro(null);
    minhasCotacoes(aplicada, ctl.signal)
      .then((c) => { if (vivo) setCotacoes(c); })
      .catch((e: unknown) => { if (vivo) setErro(mensagem(e, 'Falha ao carregar as cotações.')); });
    return () => { vivo = false; ctl.abort(); };
  }, [aplicada]);

  async function abrir(id: string) {
    try { aoAbrir(await lerCotacao(id)); }
    catch (e) { avisar(mensagem(e, 'Falha ao abrir a cotação.'), 'erro'); }
  }

  return (
    <Painel titulo="Minhas Cotações" acoes={
      <button type="button" className="botao-secundario" onClick={() => setAplicada(busca)}>Atualizar</button>
    }>
      <Campo id="portal-busca" rotulo="Pesquisar pelo número">
        <input id="portal-busca" placeholder="ex.: RFQ-2026-000001" value={busca}
          onChange={(e) => setBusca(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter') setAplicada(busca); }} />
      </Campo>

      {erro && <Erro>{erro}</Erro>}
      {!cotacoes && !erro && <Carregando />}
      {cotacoes && !cotacoes.length && <Vazio>Nenhuma cotação localizada para o seu cadastro.</Vazio>}

      {cotacoes && cotacoes.length > 0 && (
        <div className="mt-3 overflow-x-auto">
          <table data-testid="cotacoes-do-portal" className="min-w-[640px]">
            <thead>
              <tr><th>Número</th><th>Tipo</th><th>Prazo</th><th>Situação</th><th>Minhas propostas</th><th></th></tr>
            </thead>
            <tbody>
              {cotacoes.map((c) => (
                <tr key={c.id} data-cotacao={c.number}>
                  <td className="whitespace-nowrap"><strong>{c.number}</strong></td>
                  <td>{c.kind}</td>
                  <td className="whitespace-nowrap">{data(c.deadline)}</td>
                  <td>
                    <Badge classe={c.open ? 'bg-ok-fundo text-ok' : 'bg-slate-100 text-slate-500'}>{c.status}</Badge>
                  </td>
                  <td className="sub">
                    {c.myProposals.length ? `${c.myProposals.length} versão(ões)` : '—'}
                  </td>
                  <td>
                    <button type="button" className="botao" onClick={() => abrir(c.id)}>Abrir</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Painel>
  );
}

function Detalhe({ cotacao, aoVoltar, aoAtualizar }: {
  cotacao: CotacaoDoPortal;
  aoVoltar: () => void;
  aoAtualizar: (c: CotacaoDoPortal) => void;
}) {
  const { avisar } = useToast();
  const [precos, setPrecos] = useState<Record<string, string>>({});
  const [campos, setCampos] = useState(CAMPOS_VAZIOS);
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [enviando, setEnviando] = useState(false);

  const campo = (k: keyof typeof CAMPOS_VAZIOS) => ({
    value: campos[k],
    onChange: (e: { target: { value: string } }) => setCampos((c) => ({ ...c, [k]: e.target.value })),
  });

  async function enviar() {
    const { proposta, erro } = montarPropostaDoPortal(cotacao.items, precos, campos);
    if (erro || !proposta) { avisar(erro ?? 'Proposta incompleta.', 'erro'); return; }
    setEnviando(true);
    try {
      const resultado = await enviarProposta(cotacao.id, proposta);
      // o anexo é um passo à parte: falhar nele não desfaz a proposta enviada
      if (arquivo) {
        try { await anexarNaProposta(resultado.id, arquivo); }
        catch (e) {
          avisar(`Proposta v${resultado.version} enviada, mas o anexo falhou: ${mensagem(e, 'erro no upload')}.`, 'erro');
          aoAtualizar(await lerCotacao(cotacao.id));
          return;
        }
      }
      avisar(`Proposta v${resultado.version} enviada — total ${moeda(resultado.totalValue)}.`);
      setPrecos({});
      setCampos(CAMPOS_VAZIOS);
      setArquivo(null);
      aoAtualizar(await lerCotacao(cotacao.id));
    } catch (e) { avisar(mensagem(e, 'Falha ao enviar a proposta.'), 'erro'); }
    finally { setEnviando(false); }
  }

  return (
    <>
      <Painel titulo={
        <span className="flex flex-wrap items-center gap-2">
          {cotacao.number}
          <Badge classe={cotacao.open ? 'bg-ok-fundo text-ok' : 'bg-slate-100 text-slate-500'}>{cotacao.status}</Badge>
        </span>
      } acoes={<button type="button" className="botao-secundario" onClick={aoVoltar}>← Minhas cotações</button>}>
        <Nota>
          {cotacao.kind} · prazo para resposta: {cotacao.deadline ? data(cotacao.deadline) : 'a combinar'}
          {cotacao.notes && ` · ${cotacao.notes}`}
        </Nota>

        <h3 className="mb-2 mt-4 text-[13px] font-bold uppercase tracking-wide text-texto-suave">Itens solicitados</h3>
        <div className="overflow-x-auto">
          <table data-testid="itens-solicitados">
            <thead><tr><th>#</th><th>Descrição</th><th>Qtd</th><th>Unid.</th></tr></thead>
            <tbody>
              {cotacao.items.map((i) => (
                <tr key={i.id}>
                  <td>{i.sequence}</td>
                  <td>{i.description}</td>
                  <td className="whitespace-nowrap">{quantidade(i.quantity)}</td>
                  <td>{i.unitOfMeasure}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Painel>

      {cotacao.open && (
        <Painel titulo={cotacao.myProposals.length
          ? 'Enviar proposta (a nova versão substitui a anterior na análise)'
          : 'Enviar proposta'}>
          <div className="overflow-x-auto">
            <table data-testid="form-precos">
              <tbody>
                {cotacao.items.map((i) => (
                  <tr key={i.id}>
                    <td>
                      {i.description} <span className="sub">({quantidade(i.quantity)} {i.unitOfMeasure})</span>
                    </td>
                    <td className="w-40">
                      <input type="number" min="0" step="0.01" placeholder="R$ unitário" required
                        aria-label={`Preço unitário de ${i.description}`}
                        value={precos[i.id] ?? ''}
                        onChange={(e) => setPrecos((p) => ({ ...p, [i.id]: e.target.value }))} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Grade2 className="mt-3">
            <Campo id="pp-entrega" rotulo="Prazo de entrega (dias)">
              <input id="pp-entrega" type="number" min="0" {...campo('prazoEntrega')} />
            </Campo>
            <Campo id="pp-pagamento" rotulo="Condição de pagamento">
              <input id="pp-pagamento" placeholder="ex.: 28 dias / boleto" {...campo('condicaoPagamento')} />
            </Campo>
          </Grade2>
          <Grade2 className="mt-3">
            <Campo id="pp-frete" rotulo="Frete (R$)">
              <input id="pp-frete" type="number" min="0" step="0.01" placeholder="0,00" {...campo('frete')} />
            </Campo>
            <Campo id="pp-validade" rotulo="Validade da proposta">
              <input id="pp-validade" type="date" {...campo('validade')} />
            </Campo>
          </Grade2>
          <Campo id="pp-obs" rotulo="Observações" className="mt-3">
            <input id="pp-obs" placeholder="opcional" {...campo('observacao')} />
          </Campo>
          <Campo id="pp-arquivo" rotulo="Anexo da proposta comercial" dica="PDF, imagem ou Office, até 10 MB" className="mt-3">
            <input id="pp-arquivo" type="file" accept=".pdf,.png,.jpg,.jpeg,.xlsx,.docx"
              onChange={(e) => setArquivo(e.target.files?.[0] ?? null)} />
          </Campo>

          <button type="button" className="botao mt-4 w-full" disabled={enviando} onClick={enviar}>
            {enviando ? 'Enviando…' : 'Enviar proposta'}
          </button>
        </Painel>
      )}

      <Painel titulo="Histórico das minhas propostas">
        {!cotacao.myProposals.length && <Vazio>Nenhuma proposta enviada ainda.</Vazio>}
        {cotacao.myProposals.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="historico-propostas" className="min-w-[620px]">
              <thead>
                <tr><th>Versão</th><th>Total</th><th>Prazo</th><th>Pagamento</th><th>Enviada em</th><th>Anexo</th></tr>
              </thead>
              <tbody>
                {cotacao.myProposals.map((p) => (
                  <tr key={p.id}>
                    <td>v{p.version}</td>
                    <td className="whitespace-nowrap"><strong>{moeda(p.totalValue)}</strong></td>
                    <td className="whitespace-nowrap">{p.deliveryDays != null ? `${p.deliveryDays} dias` : '—'}</td>
                    <td>{p.paymentTerms || '—'}</td>
                    <td className="sub whitespace-nowrap">{dataHora(p.submittedAt)}</td>
                    <td className="sub">{p.attachmentFileName || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>
    </>
  );
}

export function Portal() {
  const [fornecedor, setFornecedor] = useState<Fornecedor | null>(null);
  const [aberta, setAberta] = useState<CotacaoDoPortal | null>(null);
  const [restaurando, setRestaurando] = useState(sessaoPortal.ativa);

  // a sessão do portal vive só nesta aba; um 401 em qualquer chamada volta ao login
  useEffect(() => {
    let vivo = true;
    if (sessaoPortal.ativa) {
      minhasCotacoes('')
        .then(() => { if (vivo) setFornecedor((f) => f ?? { id: '', name: 'Fornecedor', taxId: '' }); })
        .catch(() => { if (vivo) setFornecedor(null); })
        .finally(() => { if (vivo) setRestaurando(false); });
    }
    const expirou = () => { if (vivo) { setFornecedor(null); setAberta(null); } };
    globalThis.addEventListener(EVENTO_PORTAL_EXPIRADO, expirou);
    return () => { vivo = false; globalThis.removeEventListener(EVENTO_PORTAL_EXPIRADO, expirou); };
  }, []);

  function sair() {
    sairDoPortal();
    setFornecedor(null);
    setAberta(null);
  }

  return (
    <div className="min-h-screen bg-superficie-suave">
      <Topo fornecedor={fornecedor} aoSair={sair} />
      <main className="mx-auto max-w-[880px] px-4 py-6">
        {restaurando && !fornecedor && <Painel><Carregando texto="Abrindo o portal…" /></Painel>}
        {!restaurando && !fornecedor && <Login aoEntrar={setFornecedor} />}
        {fornecedor && !aberta && <Lista aoAbrir={setAberta} />}
        {fornecedor && aberta && (
          <Detalhe cotacao={aberta} aoVoltar={() => setAberta(null)} aoAtualizar={setAberta} />
        )}
      </main>
    </div>
  );
}
