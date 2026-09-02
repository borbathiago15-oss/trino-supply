import { useState, type FormEvent } from 'react';
import {
  atualizarEmpresa, criarEmpresa, formatarCnpj, listarEmpresas, perfilDaEmpresa, salvarPerfilDaEmpresa,
  type DadosEmpresa, type Empresa, type PerfilDaEmpresa,
} from '@/api/empresas';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { BadgeAtivo, Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';

const VAZIO = {
  razao: '', cnpj: '', endereco: '', bairro: '', cidade: '', uf: '', cep: '',
  ie: '', telefone: '', email: '',
};
type Formulario = typeof VAZIO;
const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

const dadosDoFormulario = (f: Formulario): DadosEmpresa => ({
  legalName: f.razao,
  stateRegistration: f.ie || null,
  address: f.endereco,
  district: f.bairro || null,
  city: f.cidade,
  state: f.uf,
  zip: f.cep,
  phone: f.telefone || null,
  email: f.email || null,
});

export function Empresas() {
  const { avisar } = useToast();
  const [editando, setEditando] = useState<Empresa | null>(null);
  const [form, setForm] = useState<Formulario>(VAZIO);
  const [salvando, setSalvando] = useState(false);

  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarEmpresas(true, signal), [],
  );

  function editar(c: Empresa) {
    setEditando(c);
    setForm({
      razao: c.legalName, cnpj: formatarCnpj(c.taxId), endereco: c.address, bairro: c.district ?? '',
      cidade: c.city, uf: c.state, cep: c.zip, ie: c.stateRegistration ?? '',
      telefone: c.phone ?? '', email: c.email ?? '',
    });
    rolarPara('form-empresa');
  }
  const cancelar = () => { setEditando(null); setForm(VAZIO); };

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      if (editando) {
        await atualizarEmpresa(editando.id, dadosDoFormulario(form));
        avisar('Empresa atualizada.');
        cancelar();
      } else {
        await criarEmpresa({ ...dadosDoFormulario(form), taxId: form.cnpj });
        avisar('CNPJ cadastrado — ele já aparece nos vínculos de centro de custo.');
        setForm(VAZIO);
      }
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar a empresa.'), 'erro'); }
    finally { setSalvando(false); }
  }

  async function alternarSituacao(c: Empresa) {
    try {
      await atualizarEmpresa(c.id, { active: !c.active });
      avisar('Empresa atualizada.');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao atualizar.'), 'erro'); }
  }

  const campo = (k: keyof Formulario) => ({
    value: form[k],
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  return (
    <>
      <Painel titulo="CNPJs do grupo">
        <Nota>Cada centro de custo pode apontar para um CNPJ — a O.C. daquele centro usa os dados dessa empresa no cabeçalho.</Nota>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !dados.length && <Vazio>Nenhum CNPJ cadastrado ainda — cadastre as empresas do grupo abaixo.</Vazio>}
        {dados && dados.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-empresas">
              <thead>
                <tr><th>Razão social</th><th>CNPJ</th><th>Cidade/UF</th><th>Situação</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {dados.map((c) => (
                  <tr key={c.id} data-empresa={c.taxId}>
                    <td>
                      <span className="font-semibold">{c.legalName}</span>
                      {c.email && <div className="sub">{c.email}</div>}
                    </td>
                    <td className="whitespace-nowrap">{formatarCnpj(c.taxId)}</td>
                    <td>{c.city}/{c.state}</td>
                    <td><BadgeAtivo ativo={c.active} rotuloAtivo="ATIVA" rotuloInativo="INATIVA" /></td>
                    <td className="whitespace-nowrap">
                      <div className="flex gap-1.5">
                        <button type="button" className="botao-secundario" onClick={() => editar(c)}>Editar</button>
                        <button type="button" className={c.active ? 'botao-perigo' : 'botao-secundario'}
                          onClick={() => alternarSituacao(c)}>{c.active ? 'Inativar' : 'Reativar'}</button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      <Painel id="form-empresa" titulo={editando ? `Editar CNPJ — ${editando.legalName}` : 'Novo CNPJ'}>
        <form onSubmit={enviar}>
          <Grade2>
            <Campo id="emp-razao" rotulo="Razão social">
              <input id="emp-razao" required minLength={3} {...campo('razao')} />
            </Campo>
            <Campo id="emp-cnpj" rotulo="CNPJ">
              <input id="emp-cnpj" required placeholder="14 dígitos" disabled={!!editando}
                title={editando ? 'O CNPJ é a identidade da empresa e não muda.' : undefined} {...campo('cnpj')} />
            </Campo>
          </Grade2>
          <Grade2 className="mt-3">
            <Campo id="emp-endereco" rotulo="Endereço">
              <input id="emp-endereco" required {...campo('endereco')} />
            </Campo>
            <Campo id="emp-bairro" rotulo="Bairro/Distrito">
              <input id="emp-bairro" {...campo('bairro')} />
            </Campo>
          </Grade2>
          <Grade2 className="mt-3">
            <Campo id="emp-cidade" rotulo="Cidade">
              <input id="emp-cidade" required {...campo('cidade')} />
            </Campo>
            <Grade2>
              <Campo id="emp-uf" rotulo="UF">
                <input id="emp-uf" required minLength={2} maxLength={2} {...campo('uf')} />
              </Campo>
              <Campo id="emp-cep" rotulo="CEP">
                <input id="emp-cep" required {...campo('cep')} />
              </Campo>
            </Grade2>
          </Grade2>
          <Grade2 className="mt-3">
            <Campo id="emp-ie" rotulo="Inscrição Estadual">
              <input id="emp-ie" {...campo('ie')} />
            </Campo>
            <Campo id="emp-telefone" rotulo="Fone">
              <input id="emp-telefone" {...campo('telefone')} />
            </Campo>
          </Grade2>
          <Campo id="emp-email" rotulo="E-mail de compras" className="mt-3">
            <input id="emp-email" type="email" {...campo('email')} />
          </Campo>
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="submit" className="botao" disabled={salvando}>
              {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Cadastrar CNPJ'}
            </button>
            {editando && <button type="button" className="botao-secundario" onClick={cancelar}>Cancelar edição</button>}
          </div>
        </form>
      </Painel>

      <PadraoDaOc />
    </>
  );
}

const PERFIL_VAZIO: PerfilDaEmpresa = {
  legalName: '', taxId: '', stateRegistration: null, address: '', district: null, city: '', state: '', zip: '',
  phone: null, email: null, deliveryAddress: null, deliveryTaxId: null, standardClauses: null, paymentPolicy: null,
};

/** Cabeçalho de reserva da O.C., cláusulas e política de pagamento do PDF. */
function PadraoDaOc() {
  const { avisar } = useToast();
  const [perfil, setPerfil] = useState<PerfilDaEmpresa | null>(null);
  const [salvando, setSalvando] = useState(false);

  const { erro, carregando } = useCarregar(async (signal) => {
    const p = await perfilDaEmpresa(signal);
    setPerfil({ ...PERFIL_VAZIO, ...p });
    return p;
  }, []);

  if (erro) return <Painel titulo="Padrão da O.C."><Erro>{erro}</Erro></Painel>;
  if (carregando && !perfil) return <Painel titulo="Padrão da O.C."><Carregando /></Painel>;
  if (!perfil) return null;

  const campo = (k: keyof PerfilDaEmpresa) => ({
    value: perfil[k] ?? '',
    onChange: (ev: { target: { value: string } }) => setPerfil((p) => (p ? { ...p, [k]: ev.target.value } : p)),
  });

  async function salvar(ev: FormEvent) {
    ev.preventDefault();
    if (!perfil) return;
    setSalvando(true);
    try {
      await salvarPerfilDaEmpresa(perfil);
      avisar('Dados da empresa salvos — usados no cabeçalho e na política do PDF da O.C.');
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar os dados da empresa.'), 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <Painel id="padrao-oc" titulo="Padrão da O.C. — cabeçalho de reserva, cláusulas e política de pagamento">
      <Nota>
        Usado quando o centro de custo do processo não tem CNPJ vinculado; as cláusulas e a política valem para todas as O.C.s.
      </Nota>
      <form onSubmit={salvar} className="mt-3">
        <Grade2>
          <Campo id="oc-razao" rotulo="Razão social"><input id="oc-razao" required {...campo('legalName')} /></Campo>
          <Campo id="oc-cnpj" rotulo="CNPJ"><input id="oc-cnpj" required {...campo('taxId')} /></Campo>
        </Grade2>
        <Grade2 className="mt-3">
          <Campo id="oc-endereco" rotulo="Endereço"><input id="oc-endereco" required {...campo('address')} /></Campo>
          <Campo id="oc-bairro" rotulo="Bairro/Distrito"><input id="oc-bairro" {...campo('district')} /></Campo>
        </Grade2>
        <Grade2 className="mt-3">
          <Campo id="oc-cidade" rotulo="Cidade"><input id="oc-cidade" required {...campo('city')} /></Campo>
          <Grade2>
            <Campo id="oc-uf" rotulo="UF"><input id="oc-uf" required maxLength={2} {...campo('state')} /></Campo>
            <Campo id="oc-cep" rotulo="CEP"><input id="oc-cep" required {...campo('zip')} /></Campo>
          </Grade2>
        </Grade2>
        <Grade2 className="mt-3">
          <Campo id="oc-ie" rotulo="Inscrição Estadual"><input id="oc-ie" {...campo('stateRegistration')} /></Campo>
          <Campo id="oc-fone" rotulo="Fone/Fax"><input id="oc-fone" {...campo('phone')} /></Campo>
        </Grade2>
        <Campo id="oc-email" rotulo="E-mail de compras" className="mt-3">
          <input id="oc-email" type="email" {...campo('email')} />
        </Campo>
        <Grade2 className="mt-3">
          <Campo id="oc-entrega" rotulo="Endereço de entrega (linha completa)">
            <input id="oc-entrega" placeholder="se vazio, usa o endereço acima" {...campo('deliveryAddress')} />
          </Campo>
          <Campo id="oc-entrega-cnpj" rotulo="CNPJ da entrega">
            <input id="oc-entrega-cnpj" placeholder="se vazio, usa o CNPJ acima" {...campo('deliveryTaxId')} />
          </Campo>
        </Grade2>
        <Campo id="oc-clausulas" rotulo="Cláusulas padrão da O.C." className="mt-3">
          <textarea id="oc-clausulas" rows={4} {...campo('standardClauses')} />
        </Campo>
        <Campo id="oc-politica" rotulo="Política de pagamento a fornecedores (página 2 do PDF)" className="mt-3">
          <textarea id="oc-politica" rows={8} {...campo('paymentPolicy')} />
        </Campo>
        <button type="submit" className="botao mt-4" disabled={salvando}>
          {salvando ? 'Salvando…' : 'Salvar dados da empresa'}
        </button>
      </form>
    </Painel>
  );
}
