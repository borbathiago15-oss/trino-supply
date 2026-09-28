import { useEffect, useState, type FormEvent } from 'react';
import { lerMetas, salvarMetas, type MetaDoIndicador } from '@/api/metas';
import { Carregando, Erro, Painel } from '@/componentes/basicos';
import { Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const UNIDADE: Record<MetaDoIndicador['unit'], string> = { moeda: 'R$ por mês', pct: '%', dias: 'dias', qtd: 'SCs' };

/** Vazio apaga a meta; o resto vira número, com vírgula ou ponto. */
export function valorDaMeta(texto: string): number | null {
  const limpo = texto.trim().replace(/\./g, '').replace(',', '.');
  if (limpo === '') return null;
  const n = Number(limpo);
  return Number.isFinite(n) ? n : NaN;
}

/**
 * As metas dos indicadores do painel e da diretoria. Sem meta, o card mostra só o número:
 * uma meta inventada faria "85% da meta" parecer conferido. Por isso o campo vazio é uma
 * resposta válida — é "esta empresa não tem meta para isto".
 */
export function MetasDosIndicadores() {
  const { avisar } = useToast();
  const { dados, erro, carregando, recarregar } = useCarregar(lerMetas, []);
  const [edicoes, setEdicoes] = useState<Record<string, string>>({});
  const [salvando, setSalvando] = useState(false);
  useEffect(() => { setEdicoes({}); }, [dados]);

  const texto = (m: MetaDoIndicador) =>
    edicoes[m.indicator] ?? (m.monthlyValue != null ? String(m.monthlyValue).replace('.', ',') : '');
  const pode = dados?.canEdit ?? false;

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    const goals = (dados?.goals ?? []).map((m) => ({ indicator: m.indicator, monthlyValue: valorDaMeta(texto(m)) }));
    if (goals.some((g) => Number.isNaN(g.monthlyValue))) { avisar('Digite só números nas metas.', 'erro'); return; }
    setSalvando(true);
    try { await salvarMetas(goals); avisar('Metas salvas. O painel e a diretoria já comparam com elas.'); recarregar(); }
    catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao salvar as metas.', 'erro'); }
    finally { setSalvando(false); }
  }

  if (erro) return <Painel titulo="Metas dos indicadores"><Erro>{erro}</Erro></Painel>;
  if (carregando && !dados) return <Painel titulo="Metas dos indicadores"><Carregando /></Painel>;

  return (
    <Painel titulo="Metas dos indicadores">
      <Nota>
        A meta é <b>mensal</b>. No período, o que soma (valor comprado, saving) multiplica a meta pelos
        meses; o que é média ou taxa (prazo, OTIF) usa a mesma meta. Deixe vazio para o indicador não
        ter meta — o card mostra só o número, sem comparação inventada.
      </Nota>
      <form onSubmit={enviar} className="mt-3">
        <div className="overflow-x-auto">
          <table data-testid="tabela-metas" className="min-w-[560px]">
            <thead><tr><th>Indicador</th><th>Melhor quando</th><th>Meta mensal</th><th>Última alteração</th></tr></thead>
            <tbody>
              {(dados?.goals ?? []).map((m) => (
                <tr key={m.indicator}>
                  <td><label htmlFor={`meta-${m.indicator}`}>{m.label}</label></td>
                  <td className="sub">{m.higherIsBetter ? 'maior' : 'menor'}</td>
                  <td>
                    <div className="flex items-center gap-1.5">
                      <input id={`meta-${m.indicator}`} inputMode="decimal" className="w-36" disabled={!pode}
                        placeholder="sem meta" value={texto(m)}
                        onChange={(e) => setEdicoes((x) => ({ ...x, [m.indicator]: e.target.value }))} />
                      <span className="sub">{UNIDADE[m.unit]}</span>
                    </div>
                  </td>
                  <td className="sub">{m.updatedAt ? `${m.updatedByLabel} · ${data(m.updatedAt)}` : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {pode
          ? <button type="submit" className="botao mt-4" disabled={salvando}>{salvando ? 'Salvando…' : 'Salvar metas'}</button>
          : <p className="sub mt-3">Só o administrador altera as metas.</p>}
      </form>
    </Painel>
  );
}
