import { useState, type FormEvent } from 'react';
import { atualizarSetor, criarSetor, listarSetores, type Setor } from '@/api/setores';
import { Carregando, Erro, Painel, Vazio } from '@/componentes/basicos';
import { BadgeAtivo, Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { rolarPara } from '@/util/rolar';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * O código que o servidor vai gerar quando o campo fica vazio — mostrado antes de salvar
 * para o cadastro não virar adivinhação. A regra tem de ser a mesma dos dois lados: aqui
 * e em `SectorService.DoNome`.
 */
export function codigoSugerido(nome: string): string {
  const letras = nome.normalize('NFD').replace(/[^\p{L}\p{N}]/gu, '').toUpperCase();
  return letras.length <= 6 ? letras : letras.slice(0, 6);
}

export function Setores() {
  const { avisar } = useToast();
  const [editando, setEditando] = useState<Setor | null>(null);
  const [nome, setNome] = useState('');
  const [codigo, setCodigo] = useState('');
  const [salvando, setSalvando] = useState(false);

  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => listarSetores(true, signal), [],
  );

  function editar(s: Setor) {
    setEditando(s);
    setNome(s.name);
    setCodigo(s.code);
    rolarPara('form-setor');
  }
  const cancelar = () => { setEditando(null); setNome(''); setCodigo(''); };

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      if (editando) {
        await atualizarSetor(editando.id, { name: nome });
        avisar('Setor atualizado.');
        cancelar();
      } else {
        await criarSetor({ name: nome, code: codigo || undefined });
        avisar('Setor cadastrado — ele já aparece no vínculo do usuário.');
        setNome(''); setCodigo('');
      }
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar o setor.'), 'erro'); }
    finally { setSalvando(false); }
  }

  async function alternarSituacao(s: Setor) {
    try {
      await atualizarSetor(s.id, { active: !s.active });
      avisar('Setor atualizado.');
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao atualizar.'), 'erro'); }
  }

  return (
    <>
      <Painel titulo="Setores">
        <Nota>
          O setor é <b>quem trabalha</b> — RH, TI, Manutenção. O centro de custo é onde o dinheiro
          cai: um setor atende vários centros, e um centro é atendido por vários setores.
        </Nota>
        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}
        {dados && !dados.length && <Vazio>Nenhum setor cadastrado ainda — cadastre os setores da casa abaixo.</Vazio>}
        {dados && dados.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table data-testid="tabela-setores">
              <thead>
                <tr><th>Código</th><th>Nome</th><th>Situação</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {dados.map((s) => (
                  <tr key={s.id} data-setor={s.code}>
                    <td className="whitespace-nowrap font-semibold">{s.code}</td>
                    <td>{s.name}</td>
                    <td><BadgeAtivo ativo={s.active} /></td>
                    <td className="whitespace-nowrap">
                      <div className="flex gap-1.5">
                        <button type="button" className="botao-secundario" onClick={() => editar(s)}>Editar</button>
                        <button type="button" className={s.active ? 'botao-perigo' : 'botao-secundario'}
                          onClick={() => alternarSituacao(s)}>{s.active ? 'Inativar' : 'Reativar'}</button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Painel>

      <Painel id="form-setor" titulo={editando ? `Editar setor — ${editando.code}` : 'Novo setor'}>
        <form onSubmit={enviar}>
          <Grade2>
            <Campo id="set-nome" rotulo="Nome do setor">
              <input id="set-nome" required minLength={2} value={nome}
                onChange={(ev) => setNome(ev.target.value)} />
            </Campo>
            <Campo id="set-codigo" rotulo="Código" dica={editando
              ? 'O código é a identidade do setor e não muda depois de gravado.'
              : `Se ficar vazio, será ${codigoSugerido(nome) || '—'}.`}>
              <input id="set-codigo" maxLength={20} value={codigo} disabled={!!editando}
                placeholder={codigoSugerido(nome)}
                onChange={(ev) => setCodigo(ev.target.value.toUpperCase())} />
            </Campo>
          </Grade2>
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="submit" className="botao" disabled={salvando}>
              {salvando ? 'Salvando…' : editando ? 'Salvar alterações' : 'Cadastrar setor'}
            </button>
            {editando && <button type="button" className="botao-secundario" onClick={cancelar}>Cancelar edição</button>}
          </div>
        </form>
      </Painel>
    </>
  );
}
