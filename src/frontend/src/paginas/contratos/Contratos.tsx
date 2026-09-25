import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  historicoDeReajustes, listarContratos, registrarReajuste, resumoDeContratos,
  type LinhaContrato, type Reajuste,
} from '@/api/contratos';
import { Badge, Carregando, Erro, FaixaKpis, Kpi, Painel, Vazio } from '@/componentes/basicos';
import { Dialogo } from '@/componentes/Dialogo';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { CelulaAcoes, MenuAcoes } from '@/componentes/MenuAcoes';
import { useToast } from '@/componentes/Toast';
import { data, dataHora, moeda } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * O reajuste evitado é a diferença entre o que o fornecedor pleiteou e o que
 * foi fechado — quem faz a conta é o backend, sobre a base dos últimos 12
 * meses. Aqui só validamos que os dois percentuais foram informados.
 */
export function validarPleito(pedido: string, fechado: string): string | null {
  const p = Number(pedido.replace(',', '.'));
  const f = Number(fechado.replace(',', '.'));
  if (!pedido.trim() || Number.isNaN(p) || p <= 0) return 'Informe o percentual pleiteado pelo fornecedor.';
  if (!fechado.trim() || Number.isNaN(f) || f < 0) return 'Informe o percentual fechado (0 = reajuste totalmente evitado).';
  if (f > p) return 'O percentual fechado não pode ser maior que o pleiteado.';
  return null;
}

interface FormReajuste { pedido: string; fechado: string; observacao: string; aplicar: boolean }
const FORM_VAZIO: FormReajuste = { pedido: '', fechado: '', observacao: '', aplicar: false };

export function Contratos() {
  const { avisar } = useToast();
  const [pleito, setPleito] = useState<LinhaContrato | null>(null);
  const [form, setForm] = useState<FormReajuste>(FORM_VAZIO);
  const [salvando, setSalvando] = useState(false);
  const [historico, setHistorico] = useState<
    { linha: LinhaContrato; itens: Reajuste[]; total: number } | null>(null);

  const { dados, erro, carregando, recarregar } = useCarregar(listarContratos, []);
  const linhas = dados ?? [];
  const resumo = resumoDeContratos(linhas);

  function abrirPleito(l: LinhaContrato) { setForm(FORM_VAZIO); setPleito(l); }

  async function salvarPleito() {
    if (!pleito) return;
    const problema = validarPleito(form.pedido, form.fechado);
    if (problema) { avisar(problema, 'erro'); return; }
    setSalvando(true);
    try {
      const a = await registrarReajuste(pleito.supplierId, {
        requestedPercent: Number(form.pedido.replace(',', '.')),
        agreedPercent: Number(form.fechado.replace(',', '.')),
        notes: form.observacao || null,
        applyToPrices: form.aplicar,
      });
      avisar(`Reajuste registrado — custo evitado de ${moeda(a.costAvoidance)} (base 12m ${moeda(a.baseValue)}).`);
      setPleito(null);
      recarregar();
    } catch (e) { avisar(mensagem(e, 'Falha ao registrar o reajuste.'), 'erro'); }
    finally { setSalvando(false); }
  }

  async function abrirHistorico(l: LinhaContrato) {
    try {
      const r = await historicoDeReajustes(l.supplierId);
      setHistorico({ linha: l, itens: r.items, total: r.costAvoidanceTotal });
    } catch (e) { avisar(mensagem(e, 'Falha ao ler o histórico de reajustes.'), 'erro'); }
  }

  const campo = (k: 'pedido' | 'fechado' | 'observacao') => ({
    value: form[k],
    onChange: (ev: { target: { value: string } }) => setForm((f) => ({ ...f, [k]: ev.target.value })),
  });

  return (
    <>
      <Painel titulo="Contratos de Parceria">
        <Nota>
          Os contratos são mantidos em Cadastros → Fornecedores (botão Contrato). Aqui você acompanha
          vigência, teto, consumo e saldo — cada O.C. registrada do fornecedor abate o saldo. Clique no
          fornecedor para abrir a ficha: documentos, compras e o histórico do contrato.
        </Nota>

        {erro && <Erro>{erro}</Erro>}
        {carregando && !dados && <Carregando />}

        {dados && (
          <>
            <FaixaKpis>
              <Kpi rotulo="Contratos vigentes" valor={resumo.vigentes}
                detalhe={`${resumo.foraDaVigencia} fora da vigência`} />
              <Kpi rotulo="Teto contratado" valor={moeda(resumo.tetoTotal)}
                detalhe="somando os vigentes com teto" />
              <Kpi rotulo="Consumido (O.C.s na vigência)" valor={moeda(resumo.consumido)}
                detalhe="spend sob contrato" />
              <Kpi rotulo="Saldo disponível" valor={moeda(resumo.saldo)}
                detalhe={resumo.percentualConsumido != null ? `${resumo.percentualConsumido}% consumido` : '—'} />
            </FaixaKpis>

            {!linhas.length && (
              <Vazio>Nenhum contrato de parceria cadastrado. Cadastre em Cadastros → Fornecedores → Contrato.</Vazio>
            )}

            {linhas.length > 0 && (
              <div className="overflow-x-auto">
                <table data-testid="tabela-contratos" className="min-w-[1000px]">
                  <thead>
                    <tr>
                      <th>Fornecedor</th><th>Vigência</th><th>Produtos</th><th>Teto</th>
                      <th>Consumido</th><th>Saldo</th><th>Situação</th><th>Reajuste</th>
                    </tr>
                  </thead>
                  <tbody>
                    {linhas.map((l) => {
                      const c = l.contrato;
                      return (
                        <tr key={l.supplierId} data-contrato={l.supplierName}>
                          <td className="min-w-[200px]">
                            <Link className="font-semibold text-marca hover:underline" to={`/contratos/${l.supplierId}`}
                              title="Abrir a ficha: documentos, compras e histórico do contrato">
                              {l.supplierName}
                            </Link>
                            <div className="sub">Contrato {c.number || '—'}</div>
                          </td>
                          <td className="sub whitespace-nowrap">{data(c.validFrom)} → {data(c.validUntil)}</td>
                          <td>{c.items.length}</td>
                          <td className="whitespace-nowrap">
                            {c.valueLimit != null ? moeda(c.valueLimit) : <span className="sub">sem teto</span>}
                          </td>
                          <td className="whitespace-nowrap">
                            {c.valueLimit != null ? (
                              <>
                                {moeda(c.consumed ?? 0)}
                                {l.percentualConsumido != null && <span className="sub"> ({l.percentualConsumido}%)</span>}
                              </>
                            ) : '—'}
                          </td>
                          <td className="whitespace-nowrap">
                            {c.balance != null
                              ? <strong className={l.saldoCritico ? 'text-perigo' : ''}>{moeda(c.balance)}</strong>
                              : '—'}
                          </td>
                          <td>
                            <Badge classe={c.current ? 'bg-ok-fundo text-ok' : 'bg-slate-100 text-slate-500'}>
                              {c.current ? 'VIGENTE' : 'FORA DA VIGÊNCIA'}
                            </Badge>
                          </td>
                          <td>
                            <CelulaAcoes>
                              <button type="button" className="botao-secundario" onClick={() => abrirPleito(l)}>
                                Registrar reajuste
                              </button>
                              <MenuAcoes acoes={[{ rotulo: 'Histórico de reajustes', aoEscolher: () => abrirHistorico(l) }]} />
                            </CelulaAcoes>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </Painel>

      {pleito && (
        <Dialogo titulo={`Reajuste pleiteado por ${pleito.supplierName}`} aoFechar={() => setPleito(null)} acoes={
          <>
            <button type="button" className="botao-secundario" onClick={() => setPleito(null)}>Cancelar</button>
            <button type="button" className="botao" disabled={salvando} onClick={salvarPleito}>
              {salvando ? 'Registrando…' : 'Registrar reajuste'}
            </button>
          </>
        }>
          <Grade2>
            <Campo id="ct-pedido" rotulo="Percentual pleiteado (%)">
              <input id="ct-pedido" type="number" step="0.01" min="0" {...campo('pedido')} />
            </Campo>
            <Campo id="ct-fechado" rotulo="Percentual fechado (%)" dica="0 = reajuste totalmente evitado">
              <input id="ct-fechado" type="number" step="0.01" min="0" {...campo('fechado')} />
            </Campo>
          </Grade2>
          <Campo id="ct-obs" rotulo="Observação" dica="(opcional)" className="mt-3">
            <input id="ct-obs" {...campo('observacao')} />
          </Campo>
          <label className="mt-3 flex items-center gap-2">
            <input type="checkbox" className="w-auto" checked={form.aplicar}
              onChange={(e) => setForm((f) => ({ ...f, aplicar: e.target.checked }))} />
            Aplicar o % fechado aos preços dos produtos do contrato
          </label>
          <Nota>O custo evitado é calculado sobre o que foi comprado do fornecedor nos últimos 12 meses.</Nota>
        </Dialogo>
      )}

      {historico && (
        <Dialogo titulo={`Reajustes de ${historico.linha.supplierName}`} aoFechar={() => setHistorico(null)}>
          {!historico.itens.length && <p>Nenhum reajuste registrado para este contrato.</p>}
          {historico.itens.length > 0 && (
            <>
              <p className="mb-3">Custo evitado total: <strong>{moeda(historico.total)}</strong></p>
              <ul className="space-y-2">
                {historico.itens.map((a) => (
                  <li key={a.id} className="border-b border-borda pb-2 last:border-b-0">
                    Pleiteado {a.requestedPercent}% → fechado {a.agreedPercent}% · evitado <strong>{moeda(a.costAvoidance)}</strong>
                    {a.appliedToPrices && ' · preços reajustados'}
                    <div className="sub">
                      {dataHora(a.createdAt)}{a.createdByLabel ? ` · ${a.createdByLabel}` : ''}
                      {a.notes ? ` · ${a.notes}` : ''}
                    </div>
                  </li>
                ))}
              </ul>
            </>
          )}
        </Dialogo>
      )}
    </>
  );
}
