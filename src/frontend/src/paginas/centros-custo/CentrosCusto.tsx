import { useMemo, useState, type FormEvent } from 'react';
import {
  atualizarCentroCusto, criarCentroCusto, listarCentrosCusto, type CentroCusto, type DadosCentroCusto,
} from '@/api/centrosCusto';
import { listarEmpresas, type Empresa } from '@/api/empresas';
import { listarUsuariosPicker, type UsuarioPicker } from '@/api/usuarios';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { BadgeAtivo, Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { ROTULO_PAPEL, podeManterCatalogo, temModulo, type Papel } from '@/dominio/papeis';
import { useUsuario } from '@/sessao/SessaoProvider';
import { moeda } from '@/util/formato';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';

/** Quem pode ser marcado em cada nível de alçada (mesma regra do legado). */
const PAPEIS_POR_NIVEL: Record<'level1' | 'level2', Papel[]> = {
  level1: ['Approver', 'SupplyManager', 'SystemAdministrator'],
  level2: ['Director', 'SystemAdministrator'],
};

const VAZIO = { nome: '', regional: '', gerente: '', empresa: '', cliente: '', limite1: '', limite2: '' };
type Formulario = typeof VAZIO;

/**
 * Quem aparece para marcar num nível: os papéis daquele nível, mais quem já
 * está marcado — alguém marcado antes continua visível mesmo se o papel mudou.
 */
export function candidatosDoNivel(usuarios: UsuarioPicker[], nivel: 'level1' | 'level2', marcados: string[]) {
  return usuarios.filter((u) => PAPEIS_POR_NIVEL[nivel].includes(u.role) || marcados.includes(u.id));
}

export function CentrosCusto() {
  const usuario = useUsuario();
  const mantem = podeManterCatalogo(usuario) && temModulo(usuario, 'CENTROS_CUSTO');
  const { avisar } = useToast();
  const [editando, setEditando] = useState<CentroCusto | null>(null);
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [nivel1, setNivel1] = useState<string[]>([]);
  const [nivel2, setNivel2] = useState<string[]>([]);
  const [salvando, setSalvando] = useState(false);

  const { dados, erro, carregando, recarregar } = useCarregar(
    async (signal) => ({
      centros: await listarCentrosCusto(mantem, signal),
      // pickers só existem para quem mantém o cadastro; a lista segue visível sem eles
      usuarios: mantem ? await listarUsuariosPicker(signal).catch(() => [] as UsuarioPicker[]) : [],
      empresas: await listarEmpresas(false, signal).catch(() => [] as Empresa[]),
    }),
    [mantem],
  );

  const nomeEmpresa = useMemo(() => {
    const mapa = new Map((dados?.empresas ?? []).map((e) => [e.id, e.legalName]));
    return (id: string | null) => (id ? mapa.get(id) ?? '—' : '—');
  }, [dados]);

  function editar(c: CentroCusto) {
    setEditando(c);
    setForm({
      nome: c.name, regional: c.region ?? '', gerente: c.managerUserId ?? '', empresa: c.companyId ?? '',
      cliente: c.clientName ?? '', limite1: c.level1ValueLimit?.toString() ?? '', limite2: c.level2ValueLimit?.toString() ?? '',
    });
    setNivel1(c.level1.map((a) => a.userId));
    setNivel2(c.level2.map((a) => a.userId));
    rolarPara('form-cc');
  }
  const cancelar = () => { setEditando(null); setForm(VAZIO); setNivel1([]); setNivel2([]); };

  function corpo(): DadosCentroCusto {
    const valor = (v: string) => (v ? parseFloat(v) : null);
    return {
      name: form.nome,
      region: form.regional || null,
      managerUserId: form.gerente || null,
      clientName: form.cliente || null,
      companyId: form.empresa || null,
      level1UserIds: nivel1,
      level2UserIds: nivel2,
      level1ValueLimit: valor(form.limite1),
      level2ValueLimit: valor(form.limite2),
      clearValueLimits: !form.limite1 && !form.limite2,
    };
  }

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      if (editando) {
        await atualizarCentroCusto(editando.id, corpo());
        avisar('Centro de custo atualizado.');
      } else {
        await criarCentroCusto(corpo());
        avisar('Centro de custo cadastrado.');
      }
      cancelar();
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao salvar o centro de custo.', 'erro'); }
    finally { setSalvando(false); }
  }

  async function alternarSituacao(c: CentroCusto) {
    try {
      await atualizarCentroCusto(c.id, { active: !c.active });
      avisar('Centro de custo atualizado.');
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao atualizar.', 'erro'); }
  }

  const campo = (k: keyof Formulario) => ({
    value: form[k],
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  const listaNivel = (nivel: 'level1' | 'level2', marcados: string[], marcar: (ids: string[]) => void) => {
    const candidatos = candidatosDoNivel(dados?.usuarios ?? [], nivel, marcados);
    if (!candidatos.length)
      return <p className="sub">Nenhum usuário com esse papel — cadastre em Cadastros → Usuários.</p>;
    return (
      <div className="flex max-h-52 flex-col gap-1 overflow-y-auto rounded-lg border border-borda p-2">
        {candidatos.map((u) => (
          <label key={u.id} className="!mb-0 flex items-center gap-2 !text-[13px] !font-normal !text-texto">
            <input type="checkbox" className="!w-auto" checked={marcados.includes(u.id)}
              onChange={(ev) => marcar(ev.target.checked ? [...marcados, u.id] : marcados.filter((x) => x !== u.id))} />
            {u.name} <span className="sub">({ROTULO_PAPEL[u.role] ?? u.role})</span>
          </label>
        ))}
      </div>
    );
  };

  return (
    <>
      <Painel titulo="Centros de Custo">
        <Nota>As dimensões Regional, Gerente e Cliente dos dashboards vêm deste cadastro.</Nota>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !dados.centros.length && <Vazio>Nenhum centro de custo cadastrado ainda.</Vazio>}
        {dados && dados.centros.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-centros-custo" className="min-w-[1180px]">
              <thead>
                <tr>
                  <th>Código</th><th>Nome</th><th>Regional</th><th>Gerente</th><th>Nível 1</th><th>Nível 2</th>
                  <th>Limites</th><th>CNPJ de compras</th><th>Cliente</th><th>Situação</th>{mantem && <th>Ações</th>}
                </tr>
              </thead>
              <tbody>
                {dados.centros.map((c) => (
                  <tr key={c.id} data-centro={c.code}>
                    <td className="font-semibold">{c.code}</td>
                    <td>{c.name}</td>
                    <td>{c.region || '—'}</td>
                    <td>{c.managerName || '—'}</td>
                    <td className="sub">{c.level1.map((a) => a.name).join(', ') || 'gerente responsável'}</td>
                    <td className="sub">{c.level2.map((a) => a.name).join(', ') || 'diretor vinculado'}</td>
                    <td className="sub whitespace-nowrap">
                      {c.level1ValueLimit == null && c.level2ValueLimit == null ? 'sem limite' : (
                        <>
                          {c.level1ValueLimit != null && <div>N1 {moeda(c.level1ValueLimit)}</div>}
                          {c.level2ValueLimit != null && <div>N2 {moeda(c.level2ValueLimit)}</div>}
                        </>
                      )}
                    </td>
                    <td className="sub">{nomeEmpresa(c.companyId)}</td>
                    <td>{c.clientName || '—'}</td>
                    <td><BadgeAtivo ativo={c.active} /></td>
                    {mantem && (
                      <td className="whitespace-nowrap">
                        <div className="flex gap-1.5">
                          <button type="button" className="botao-secundario !py-1.5" onClick={() => editar(c)}>Editar</button>
                          <button type="button" className={(c.active ? 'botao-perigo' : 'botao-secundario') + ' !py-1.5'}
                            onClick={() => alternarSituacao(c)}>{c.active ? 'Inativar' : 'Reativar'}</button>
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
        <Painel id="form-cc" titulo={editando ? `Editar centro de custo — ${editando.code}` : 'Novo centro de custo'}>
          <form onSubmit={enviar}>
            <Grade2>
              <Campo id="cc-nome" rotulo="Nome">
                <input id="cc-nome" required minLength={3} placeholder="ex.: PepsiCo Simões Filho" {...campo('nome')} />
              </Campo>
              <Campo id="cc-regional" rotulo="Regional">
                <input id="cc-regional" placeholder="ex.: BAHIA" {...campo('regional')} />
              </Campo>
            </Grade2>
            <Nota>O <strong>código é gerado automaticamente</strong> a partir da regional (ex.: BAH-001).</Nota>

            <Grade2 className="mt-3">
              <Campo id="cc-gerente" rotulo="Gerente responsável (usuário)" dica="(dimensão dos dashboards)">
                <select id="cc-gerente" {...campo('gerente')}>
                  <option value="">Selecione o gerente…</option>
                  {(dados?.usuarios ?? []).map((u) => (
                    <option key={u.id} value={u.id}>{u.name} ({ROTULO_PAPEL[u.role] ?? u.role})</option>
                  ))}
                </select>
              </Campo>
              <Campo id="cc-empresa" rotulo="CNPJ de compras (empresa do grupo)">
                <select id="cc-empresa" {...campo('empresa')}>
                  <option value="">Usar padrão da OC</option>
                  {(dados?.empresas ?? []).map((e) => (
                    <option key={e.id} value={e.id}>{e.legalName} — {e.taxId}</option>
                  ))}
                </select>
              </Campo>
            </Grade2>

            <Campo id="cc-cliente" rotulo="Cliente" className="mt-3">
              <input id="cc-cliente" placeholder="opcional — cliente/contrato atendido" {...campo('cliente')} />
            </Campo>

            <p className="mb-1 mt-5 text-[12.5px] font-semibold text-texto-suave">
              Quem aprova neste centro <span className="font-normal">(marque as pessoas; qualquer uma delas resolve a etapa)</span>
            </p>
            <Grade2>
              <div>
                <p className="mb-1 text-[12px] font-semibold text-texto-suave">
                  Nível 1 — libera a solicitação <span className="font-normal">(aprovadores e gestores)</span>
                </p>
                {listaNivel('level1', nivel1, setNivel1)}
              </div>
              <div>
                <p className="mb-1 text-[12px] font-semibold text-texto-suave">
                  Nível 2 — libera a compra <span className="font-normal">(diretoria)</span>
                </p>
                {listaNivel('level2', nivel2, setNivel2)}
              </div>
            </Grade2>
            <Nota>
              Sem ninguém marcado, o centro segue como hoje: Nível 1 com o gerente responsável e Nível 2 com o
              diretor vinculado a ele.
            </Nota>

            <p className="mb-1 mt-5 text-[12.5px] font-semibold text-texto-suave">
              Limite de valor por nível <span className="font-normal">
                (opcional — só mede: processo acima do limite vira penalidade no Compliance, nada é bloqueado)</span>
            </p>
            <Grade2>
              <Campo id="cc-limite1" rotulo="Nível 1 (R$)">
                <input id="cc-limite1" type="number" min={0} step="0.01" placeholder="ex.: 50000" {...campo('limite1')} />
              </Campo>
              <Campo id="cc-limite2" rotulo="Nível 2 (R$)">
                <input id="cc-limite2" type="number" min={0} step="0.01" placeholder="ex.: 200000" {...campo('limite2')} />
              </Campo>
            </Grade2>

            <div className="mt-4 flex flex-wrap gap-2">
              <button type="submit" className="botao" disabled={salvando}>
                {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Cadastrar centro de custo'}
              </button>
              {editando && <button type="button" className="botao-secundario" onClick={cancelar}>Cancelar edição</button>}
            </div>
          </form>
        </Painel>
      )}
    </>
  );
}
