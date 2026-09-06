import { useMemo, useState, type FormEvent } from 'react';
import { listarCentrosCusto, type CentroCusto } from '@/api/centrosCusto';
import {
  atualizarUsuario, criarUsuario, listarUsuarios, redefinirSenha, TAMANHO_MINIMO_SENHA,
  type DadosUsuario, type UsuarioCadastro,
} from '@/api/usuarios';
import { Badge, Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { Confirmacao, Dialogo } from '@/componentes/Dialogo';
import { BadgeAtivo, Campo, Grade2, Nota } from '@/componentes/formulario';
import { CelulaAcoes, MenuAcoes } from '@/componentes/MenuAcoes';
import { useToast } from '@/componentes/Toast';
import {
  MODULOS_PADRAO, PAPEIS_OCULTOS, ROTULO_MODULO, ROTULO_PAPEL, type Modulo, type Papel,
} from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';

const VAZIO = { nome: '', email: '', papel: '' as Papel | '', senha: '', diretor: '' };
type Formulario = typeof VAZIO;
const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/** Quem pode ser diretor responsável de outro usuário. */
export const diretoresPossiveis = (usuarios: UsuarioCadastro[]) =>
  usuarios.filter((u) => u.active && (u.role === 'Director' || u.role === 'SystemAdministrator'));

/** Papéis oferecidos no cadastro, sem os que hoje são resolvidos por módulo. */
export const papeisOferecidos = (papeis: Papel[]) => papeis.filter((p) => !PAPEIS_OCULTOS.includes(p));

export function Usuarios() {
  const eu = useUsuario();
  const { avisar } = useToast();
  const [editando, setEditando] = useState<UsuarioCadastro | null>(null);
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [modulos, setModulos] = useState<Modulo[]>([]);
  const [centros, setCentros] = useState<string[]>([]);
  const [salvando, setSalvando] = useState(false);
  const [aInativar, setAInativar] = useState<UsuarioCadastro | null>(null);
  const [aTrocarSenha, setATrocarSenha] = useState<UsuarioCadastro | null>(null);
  const [novaSenha, setNovaSenha] = useState('');

  const { dados, erro, carregando, recarregar } = useCarregar(
    async (signal) => ({
      usuarios: await listarUsuarios(signal),
      centros: await listarCentrosCusto(false, signal).catch(() => [] as CentroCusto[]),
    }),
    [],
  );

  const lista = useMemo(() => dados?.usuarios.items ?? [], [dados]);
  const papeis = useMemo(() => papeisOferecidos(dados?.usuarios.roles ?? []), [dados]);
  const diretores = useMemo(() => diretoresPossiveis(lista), [lista]);
  const nomeDiretor = (id: string | null) => lista.find((u) => u.id === id)?.name ?? '—';

  function editar(u: UsuarioCadastro) {
    setEditando(u);
    setForm({ nome: u.name, email: u.email, papel: u.role, senha: '', diretor: u.directorId ?? '' });
    setModulos(u.modules);
    setCentros(u.costCenters);
    rolarPara('form-usuario');
  }
  const cancelar = () => { setEditando(null); setForm(VAZIO); setModulos([]); setCentros([]); };

  /** Trocar o papel de um usuário novo sugere as autorizações daquele papel. */
  function escolherPapel(papel: Papel | '') {
    setForm((f) => ({ ...f, papel }));
    if (!editando && papel) setModulos(MODULOS_PADRAO[papel] ?? []);
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    if (!form.papel) return;
    const comum: DadosUsuario = {
      name: form.nome, role: form.papel, modules: modulos, costCenters: centros,
      directorId: form.diretor || null,
    };
    setSalvando(true);
    try {
      if (editando) {
        await atualizarUsuario(editando.id, { ...comum, clearDirector: !form.diretor });
        avisar('Usuário atualizado. Autorizações valem a partir do próximo login.');
        cancelar();
      } else {
        await criarUsuario({ ...comum, email: form.email, password: form.senha });
        avisar('Usuário criado com autorizações e vínculos selecionados.');
        setForm(VAZIO); setModulos([]); setCentros([]);
      }
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar o usuário.'), 'erro'); }
    finally { setSalvando(false); }
  }

  async function alternarSituacao(u: UsuarioCadastro, ativar: boolean) {
    setAInativar(null);
    try {
      await atualizarUsuario(u.id, { active: ativar });
      avisar(ativar ? 'Usuário reativado.' : 'Usuário inativado.');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao atualizar.'), 'erro'); }
  }

  async function trocarSenha() {
    if (!aTrocarSenha) return;
    if (novaSenha.length < TAMANHO_MINIMO_SENHA) {
      avisar(`A senha precisa de pelo menos ${TAMANHO_MINIMO_SENHA} caracteres.`, 'erro');
      return;
    }
    try {
      await redefinirSenha(aTrocarSenha.id, novaSenha);
      avisar('Senha redefinida. As sessões do usuário foram encerradas.');
      setATrocarSenha(null); setNovaSenha('');
    } catch (e) { avisar(mensagem(e, 'Falha ao redefinir a senha.'), 'erro'); }
  }

  const campo = (k: keyof Formulario) => ({
    value: form[k] as string,
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  const alternar = <T,>(lista: T[], valor: T) =>
    lista.includes(valor) ? lista.filter((x) => x !== valor) : [...lista, valor];

  return (
    <>
      <Painel titulo="Usuários">
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !lista.length && (
          <Vazio>Nenhum usuário cadastrado ainda — cadastre o primeiro no formulário abaixo.</Vazio>
        )}
        {lista.length > 0 && (
          <div className="overflow-x-auto">
            <table data-testid="tabela-usuarios" className="min-w-[1040px]">
              <thead>
                <tr>
                  <th>Usuário</th><th>Papel</th><th>Autorizações</th>
                  <th>Vínculos (CC / Diretor)</th><th>Situação</th><th>Ações</th>
                </tr>
              </thead>
              <tbody>
                {lista.map((u) => (
                  <tr key={u.id} data-usuario={u.email}>
                    <td className="min-w-[200px]">
                      <span className="font-semibold">{u.name}</span>
                      <div className="sub">{u.email}</div>
                    </td>
                    <td>{ROTULO_PAPEL[u.role] ?? u.role}</td>
                    <td className="sub min-w-[220px]">{u.modules.map((m) => ROTULO_MODULO[m] ?? m).join(' · ') || '—'}</td>
                    <td className="sub">
                      {u.costCenters.join(' · ') || '—'}
                      {u.directorId && <div>Diretor: {nomeDiretor(u.directorId)}</div>}
                    </td>
                    <td>
                      <BadgeAtivo ativo={u.active} />
                      {u.mustChangePassword && (
                        <div className="mt-1">
                          <Badge classe="bg-aviso-fundo text-aviso" title="A pessoa ainda não definiu a própria senha">
                            senha provisória
                          </Badge>
                        </div>
                      )}
                    </td>
                    <td className="whitespace-nowrap">
                      <CelulaAcoes>
                        <button type="button" className="botao-secundario" onClick={() => editar(u)}>Editar</button>
                        <MenuAcoes rotulo={`Mais ações de ${u.name}`} acoes={[
                          { rotulo: 'Nova senha', aoEscolher: () => { setNovaSenha(''); setATrocarSenha(u); } },
                          u.active
                            ? { rotulo: 'Inativar', perigo: true, aoEscolher: () => setAInativar(u) }
                            : { rotulo: 'Reativar', aoEscolher: () => alternarSituacao(u, true) },
                        ]} />
                      </CelulaAcoes>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      <Painel id="form-usuario" titulo={editando ? `Editar usuário — ${editando.name}` : 'Novo usuário'}>
        <form onSubmit={enviar}>
          <Grade2>
            <Campo id="usu-nome" rotulo="Nome">
              <input id="usu-nome" required minLength={2} {...campo('nome')} />
            </Campo>
            <Campo id="usu-email" rotulo="E-mail">
              <input id="usu-email" type="email" required={!editando} disabled={!!editando}
                title={editando ? 'O e-mail identifica o usuário e não muda.' : undefined} {...campo('email')} />
            </Campo>
          </Grade2>
          <Grade2 className="mt-3">
            <Campo id="usu-papel" rotulo="Papel">
              <select id="usu-papel" required value={form.papel} onChange={(e) => escolherPapel(e.target.value as Papel | '')}>
                <option value="">Selecione o papel…</option>
                {papeis.map((p) => <option key={p} value={p}>{ROTULO_PAPEL[p] ?? p}</option>)}
              </select>
            </Campo>
            <Campo id="usu-senha" rotulo={`Senha provisória (mín. ${TAMANHO_MINIMO_SENHA})`}
              dica={editando ? undefined : '— a pessoa define a dela no primeiro acesso'}>
              <input id="usu-senha" type="password" autoComplete="new-password"
                required={!editando} disabled={!!editando} minLength={TAMANHO_MINIMO_SENHA}
                title={editando ? 'Use “Nova senha” na lista para trocar a senha.' : undefined} {...campo('senha')} />
            </Campo>
          </Grade2>

          <p className="mb-1 mt-5 text-[12.5px] font-semibold text-texto-suave">Autorizações — este usuário poderá usar:</p>
          <div className="grid grid-cols-1 gap-1 rounded-lg border border-borda p-3 sm:grid-cols-2">
            {(Object.keys(ROTULO_MODULO) as Modulo[]).map((m) => (
              <label key={m} className="!mb-0 flex items-center gap-2 !text-[13px] !font-normal !text-texto">
                <input type="checkbox" className="!w-auto" checked={modulos.includes(m)}
                  onChange={() => setModulos((l) => alternar(l, m))} />
                {ROTULO_MODULO[m]}
              </label>
            ))}
          </div>

          <p className="mb-1 mt-5 text-[12.5px] font-semibold text-texto-suave">
            Centros de custo vinculados <span className="font-normal">
              (vazio = sem restrição; solicitante e aprovador só enxergam os centros marcados)</span>
          </p>
          {dados?.centros.length ? (
            <div className="flex max-h-52 flex-col gap-1 overflow-y-auto rounded-lg border border-borda p-3">
              {dados.centros.map((c) => (
                <label key={c.id} className="!mb-0 flex items-center gap-2 !text-[13px] !font-normal !text-texto">
                  <input type="checkbox" className="!w-auto" checked={centros.includes(c.code)}
                    onChange={() => setCentros((l) => alternar(l, c.code))} />
                  {c.code} — {c.name}
                </label>
              ))}
            </div>
          ) : <p className="sub">Nenhum centro de custo cadastrado ainda.</p>}

          <Campo id="usu-diretor" className="mt-5" rotulo="Diretor responsável"
            dica="(2ª alçada dos processos deste gerente)">
            <select id="usu-diretor" {...campo('diretor')}>
              <option value="">Sem diretor vinculado</option>
              {diretores.map((d) => (
                <option key={d.id} value={d.id}>{d.name} ({ROTULO_PAPEL[d.role] ?? d.role})</option>
              ))}
            </select>
          </Campo>

          <div className="mt-4 flex flex-wrap gap-2">
            <button type="submit" className="botao" disabled={salvando}>
              {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Criar usuário'}
            </button>
            {editando && <button type="button" className="botao-secundario" onClick={cancelar}>Cancelar edição</button>}
          </div>
          {editando?.id === eu.id && <Nota>Você está editando o próprio usuário.</Nota>}
        </form>
      </Painel>

      {aInativar && (
        <Confirmacao titulo="Inativar usuário" perigo rotuloConfirmar="Inativar"
          mensagem={<>Inativar <strong>{aInativar.name}</strong>? As sessões dele serão encerradas.</>}
          aoConfirmar={() => alternarSituacao(aInativar, false)} aoFechar={() => setAInativar(null)} />
      )}
      {aTrocarSenha && (
        <Dialogo titulo={`Nova senha — ${aTrocarSenha.name}`} aoFechar={() => setATrocarSenha(null)} acoes={
          <>
            <button type="button" className="botao-secundario" onClick={() => setATrocarSenha(null)}>Cancelar</button>
            <button type="button" className="botao" onClick={trocarSenha}>Redefinir senha</button>
          </>
        }>
          <Campo id="usu-nova-senha" rotulo={`Nova senha (mínimo ${TAMANHO_MINIMO_SENHA} caracteres)`}>
            <input id="usu-nova-senha" type="password" autoComplete="new-password" value={novaSenha}
              onChange={(e) => setNovaSenha(e.target.value)} />
          </Campo>
          <Nota>
            As sessões abertas do usuário são encerradas na hora, e esta senha volta a ser
            provisória: a pessoa define a dela no próximo acesso.
          </Nota>
        </Dialogo>
      )}
    </>
  );
}
