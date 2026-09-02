import { useMemo, useState, type FormEvent } from 'react';
import {
  atualizarFornecedor, criarFornecedor, gerarChavePortal, listarFornecedores, ROTULO_HOMOLOGACAO,
  situacaoEfetiva, type Fornecedor,
} from '@/api/fornecedores';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Confirmacao, Dialogo } from '@/componentes/Dialogo';
import { BadgeAtivo, Campo, Grade2 } from '@/componentes/formulario';
import { CelulaAcoes, MenuAcoes } from '@/componentes/MenuAcoes';
import { useToast } from '@/componentes/Toast';
import { podeComprar } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { data, moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { PainelContrato } from './PainelContrato';
import { PainelHomologacao } from './PainelHomologacao';

const VAZIO = { razao: '', fantasia: '', cnpj: '', email: '', telefone: '' };
type Formulario = typeof VAZIO;
const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/** Busca por razão social, nome fantasia ou CNPJ, ignorando pontuação do documento. */
export function filtrarFornecedores(lista: Fornecedor[], busca: string) {
  const termo = busca.trim().toLowerCase();
  if (!termo) return lista;
  const so = (v: string) => v.replace(/\D/g, '');
  return lista.filter((f) =>
    [f.legalName, f.tradeName, f.email, f.phone].filter(Boolean).join(' ').toLowerCase().includes(termo)
    || f.taxId.includes(termo)
    || (so(termo).length > 0 && so(f.taxId).includes(so(termo))));
}

export function Fornecedores() {
  const usuario = useUsuario();
  const mantem = podeComprar(usuario);
  const { avisar } = useToast();
  const [busca, setBusca] = useState('');
  const [editando, setEditando] = useState<Fornecedor | null>(null);
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [salvando, setSalvando] = useState(false);
  const [homologando, setHomologando] = useState<string | null>(null);
  const [contratando, setContratando] = useState<string | null>(null);
  const [chaveDe, setChaveDe] = useState<Fornecedor | null>(null);
  const [chave, setChave] = useState<string | null>(null);

  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarFornecedores(mantem, signal), [mantem],
  );

  const lista = useMemo(() => filtrarFornecedores(dados ?? [], busca), [dados, busca]);
  const aberto = (id: string | null) => (id ? (dados ?? []).find((f) => f.id === id) ?? null : null);
  const emHomologacao = aberto(homologando);
  const emContrato = aberto(contratando);

  function editar(f: Fornecedor) {
    setEditando(f);
    setForm({ razao: f.legalName, fantasia: f.tradeName ?? '', cnpj: f.taxId, email: f.email ?? '', telefone: f.phone ?? '' });
    document.getElementById('form-fornecedor')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
  const cancelar = () => { setEditando(null); setForm(VAZIO); };

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      if (editando) {
        // razão social e CNPJ não mudam depois do cadastro
        await atualizarFornecedor(editando.id, { tradeName: form.fantasia, email: form.email, phone: form.telefone });
        avisar('Fornecedor atualizado.');
      } else {
        await criarFornecedor({
          legalName: form.razao, tradeName: form.fantasia || null, taxId: form.cnpj,
          email: form.email || null, phone: form.telefone || null,
        });
        avisar('Fornecedor cadastrado.');
      }
      cancelar();
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar o fornecedor.'), 'erro'); }
    finally { setSalvando(false); }
  }

  async function alternarSituacao(f: Fornecedor) {
    try {
      await atualizarFornecedor(f.id, { active: !f.active });
      avisar('Fornecedor atualizado.');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao atualizar.'), 'erro'); }
  }

  async function novaChave(f: Fornecedor) {
    setChaveDe(null);
    try {
      const { accessKey } = await gerarChavePortal(f.id);
      setChave(accessKey);
    } catch (e) { avisar(mensagem(e, 'Falha ao gerar a chave.'), 'erro'); }
  }

  const campo = (k: keyof Formulario) => ({
    value: form[k],
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  return (
    <>
      <Painel titulo="Fornecedores" acoes={
        <input aria-label="Buscar" placeholder="Buscar por razão social, fantasia ou CNPJ"
          className="!w-[320px]" value={busca} onChange={(e) => setBusca(e.target.value)} />
      }>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !lista.length && (
          <Vazio>{dados.length ? 'Nenhum fornecedor corresponde à busca.' : 'Nenhum fornecedor cadastrado ainda.'}</Vazio>
        )}
        {lista.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="tabela-fornecedores" className="min-w-[880px]">
              <thead>
                <tr>
                  <th>Fornecedor</th><th>CNPJ/CPF</th><th>Homologação</th>
                  <th>Contrato</th><th>Situação</th>{mantem && <th>Ações</th>}
                </tr>
              </thead>
              <tbody>
                {lista.map((f) => {
                  const { efetiva, restritoPorCertidao } = situacaoEfetiva(f);
                  const marca = ROTULO_HOMOLOGACAO[efetiva] ?? { rotulo: efetiva, classe: '' };
                  const temContrato = f.contract && f.contract.items.length > 0;
                  return (
                    <tr key={f.id} data-fornecedor={f.taxId}>
                      <td className="min-w-[240px]">
                        <span className="font-semibold">{f.legalName}</span>
                        {f.tradeName && <div className="sub">{f.tradeName}</div>}
                        {/* contato junto da identificação: uma coluna a menos para a tabela caber */}
                        {(f.email || f.phone) && (
                          <div className="sub">{[f.email, f.phone].filter(Boolean).join(' · ')}</div>
                        )}
                      </td>
                      <td className="whitespace-nowrap">{f.taxId}</td>
                      <td>
                        <Badge classe={marca.classe} title={restritoPorCertidao ? 'Restrito automático: certidão vencida' : undefined}>
                          {marca.rotulo}
                        </Badge>
                        {restritoPorCertidao && <div className="sub">certidão vencida</div>}
                      </td>
                      <td>
                        {temContrato ? (
                          <>
                            <Badge classe={f.contract.current ? 'bg-ok-fundo text-ok' : 'bg-slate-100 text-slate-500'}>
                              {f.contract.current ? 'VIGENTE' : 'FORA DA VIGÊNCIA'}
                            </Badge>
                            <div className="sub">
                              {f.contract.items.length} produto(s)
                              {f.contract.validUntil && ` · até ${data(f.contract.validUntil)}`}
                              {f.contract.balance != null && ` · saldo ${moeda(f.contract.balance)}`}
                            </div>
                          </>
                        ) : <span className="sub">sem contrato</span>}
                      </td>
                      <td><BadgeAtivo ativo={f.active} /></td>
                      {mantem && (
                        <td className="whitespace-nowrap">
                          <CelulaAcoes>
                            <button type="button" className="botao-secundario" onClick={() => editar(f)}>Editar</button>
                            <button type="button" className="botao-secundario"
                              onClick={() => { setHomologando(f.id); setContratando(null); }}>Homologação</button>
                            <MenuAcoes rotulo={`Mais ações de ${f.legalName}`} acoes={[
                              { rotulo: 'Contrato de parceria', aoEscolher: () => { setContratando(f.id); setHomologando(null); } },
                              { rotulo: 'Chave do portal', aoEscolher: () => setChaveDe(f) },
                              { rotulo: f.active ? 'Inativar' : 'Reativar', perigo: f.active, aoEscolher: () => alternarSituacao(f) },
                            ]} />
                          </CelulaAcoes>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      {mantem && (
        <Painel id="form-fornecedor" titulo={editando ? `Editar fornecedor — ${editando.legalName}` : 'Novo fornecedor'}>
          <form onSubmit={enviar}>
            <Grade2>
              <Campo id="forn-razao" rotulo="Razão social">
                <input id="forn-razao" required minLength={3} placeholder="ex.: Distribuidora Alfa LTDA"
                  disabled={!!editando} title={editando ? 'A razão social não muda depois do cadastro.' : undefined} {...campo('razao')} />
              </Campo>
              <Campo id="forn-cnpj" rotulo="CNPJ/CPF">
                <input id="forn-cnpj" required placeholder="somente números"
                  disabled={!!editando} title={editando ? 'O CNPJ não muda depois do cadastro.' : undefined} {...campo('cnpj')} />
              </Campo>
            </Grade2>
            <Grade2 className="mt-3">
              <Campo id="forn-fantasia" rotulo="Nome fantasia">
                <input id="forn-fantasia" placeholder="opcional" {...campo('fantasia')} />
              </Campo>
              <Campo id="forn-email" rotulo="E-mail">
                <input id="forn-email" type="email" placeholder="opcional" {...campo('email')} />
              </Campo>
            </Grade2>
            <Campo id="forn-telefone" rotulo="Telefone" className="mt-3">
              <input id="forn-telefone" placeholder="opcional" {...campo('telefone')} />
            </Campo>
            <div className="mt-4 flex flex-wrap gap-2">
              <button type="submit" className="botao" disabled={salvando}>
                {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Cadastrar fornecedor'}
              </button>
              {editando && <button type="button" className="botao-secundario" onClick={cancelar}>Cancelar edição</button>}
            </div>
          </form>
        </Painel>
      )}

      {emContrato && (
        <PainelContrato fornecedor={emContrato} aoSalvar={recarregar} aoFechar={() => setContratando(null)} />
      )}
      {emHomologacao && (
        <PainelHomologacao fornecedor={emHomologacao} aoSalvar={recarregar} aoFechar={() => setHomologando(null)} />
      )}

      {chaveDe && (
        <Confirmacao titulo="Chave do Portal do Fornecedor" rotuloConfirmar="Gerar nova chave"
          mensagem={<>Gerar nova chave de acesso para <strong>{chaveDe.legalName}</strong>? A chave anterior deixa de funcionar na hora.</>}
          aoConfirmar={() => novaChave(chaveDe)} aoFechar={() => setChaveDe(null)} />
      )}
      {chave && (
        <Dialogo titulo="Chave gerada" aoFechar={() => setChave(null)} acoes={
          <>
            <button type="button" className="botao-secundario"
              onClick={() => navigator.clipboard?.writeText(chave).then(() => avisar('Chave copiada.')).catch(() => {})}>
              Copiar
            </button>
            <button type="button" className="botao" onClick={() => setChave(null)}>Fechar</button>
          </>
        }>
          <p>Envie esta chave ao fornecedor. Ela <strong>não será exibida novamente</strong>.</p>
          <p className="mt-3 select-all break-all rounded-lg bg-superficie-suave px-3 py-2 font-mono text-[13px]" data-testid="chave-portal">{chave}</p>
        </Dialogo>
      )}
    </>
  );
}
