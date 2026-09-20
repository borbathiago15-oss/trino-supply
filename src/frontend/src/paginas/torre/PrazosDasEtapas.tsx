import { useEffect, useState, type FormEvent } from 'react';
import {
  leituraDoPrazo, lerPrazosDasEtapas, salvarPrazosDasEtapas,
} from '@/api/prazosDasEtapas';
import { Carregando, Erro, Painel } from '@/componentes/basicos';
import { Campo, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

/**
 * Quanto tempo cada etapa pode levar.
 *
 * <p>
 * A Torre já dizia há quantos dias a linha está parada; faltava alguém dizer que aquilo é
 * tempo demais. Seis dias em aprovação e seis dias em recebimento são coisas diferentes, e
 * sem prazo por etapa o comprador comparava o número com a própria intuição.
 * </p>
 *
 * <p>
 * O prazo <strong>mede e expõe, nunca bloqueia</strong> — a mesma decisão do Compliance
 * Score. Estourado não impede aprovar, cotar nem fechar: aparece na linha, entra no filtro e
 * conta no card. Travar o fluxo por causa do relógio empurraria o comprador para fora do
 * sistema, que é o oposto do que a Torre existe para fazer.
 * </p>
 */
export function PrazosDasEtapas() {
  const { avisar } = useToast();
  const [tipo, setTipo] = useState('');
  const { dados, erro, carregando, recarregar } = useCarregar(
    (signal) => lerPrazosDasEtapas(tipo, signal), [tipo]);
  const [dias, setDias] = useState<Record<string, string>>({});
  // as etapas que este tipo devolve ao padrão neste salvamento
  const [herdar, setHerdar] = useState<string[]>([]);
  const [salvando, setSalvando] = useState(false);

  // O valor mostrado é o editado ou, sem edição, o do servidor — derivado, não copiado num
  // efeito. Copiar deixava um quadro em que o campo já estava na tela e o número ainda não
  // tinha chegado nele; uma recarga só descarta as edições.
  useEffect(() => { setDias({}); setHerdar([]); }, [dados]);
  const etapas = dados?.items ?? [];
  const valorDe = (etapa: string) =>
    dias[etapa] ?? String(etapas.find((i) => i.stage === etapa)?.maxDays ?? '');

  const pode = dados?.canEdit ?? false;
  const invalido = etapas.some((i) => {
    const v = valorDe(i.stage);
    const n = Number(v);
    return v.trim() === '' || !Number.isInteger(n) || n < 0 || n > 365;
  });

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      // etapa marcada para herdar não vai como número: ela é apagada, e o tipo volta a
      // seguir o padrão — mandar o valor a congelaria numa cópia
      const valores = etapas
        .filter((i) => !herdar.includes(i.stage))
        .map((i) => [i.stage, Number(valorDe(i.stage))] as const);
      await salvarPrazosDasEtapas(Object.fromEntries(valores), tipo, herdar);
      avisar('Prazos atualizados. A Torre já julga por eles.');
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao salvar os prazos.', 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <Painel titulo="Prazos por Etapa">
      <Nota>
        É contra estes prazos que a Torre marca <strong>no limite</strong> e{' '}
        <strong>prazo estourado</strong>. O relógio é o da <strong>etapa</strong>, não o da
        solicitação: um item que passou vinte dias em cotação não chega à aprovação já
        estourado — lá a contagem começa do zero, senão a cobrança cairia sobre quem não teve
        culpa. <strong>Zero desliga</strong> a cobrança de tempo naquela etapa.
      </Nota>

      {dados && dados.types.length > 0 && (
        <div className="mb-3 max-w-[320px]">
          <Campo id="prazo-tipo" rotulo="Conjunto de prazos"
            dica="cada tipo de solicitação tem o seu">
            <select id="prazo-tipo" value={tipo} onChange={(e) => setTipo(e.target.value)}>
              <option value="">Padrão (vale para quem não tem tipo)</option>
              {dados.types.map((t) => (
                <option key={t.code} value={t.code}>{t.name}</option>
              ))}
            </select>
          </Campo>
        </div>
      )}

      {erro && <Erro>{erro}</Erro>}
      {carregando && !dados && <Carregando />}

      {dados && (
        <form onSubmit={enviar} data-testid="form-prazos-etapas">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            {dados.items.map((i) => (
              <Campo key={i.stage} id={`prazo-${i.stage}`} rotulo={i.label} dica="dias corridos">
                <input
                  id={`prazo-${i.stage}`} type="number" min={0} max={365} step={1}
                  disabled={!pode}
                  value={valorDe(i.stage)}
                  onChange={(e) => setDias((d) => ({ ...d, [i.stage]: e.target.value }))} />
                <p className="sub mt-1" data-testid={`leitura-${i.stage}`}>
                  {leituraDoPrazo(Number(valorDe(i.stage) || 0), dados.warnAtPercent)}
                </p>
                {/* herdado acompanha o padrão; próprio é exceção deste tipo. Sem dizer qual
                    é qual, o administrador não sabe se mexer aqui muda uma etapa ou todas */}
                {tipo !== '' && (
                  <p className="sub" data-testid={`origem-${i.stage}`}>
                    {herdar.includes(i.stage) ? 'voltará a seguir o padrão'
                      : i.inherited ? 'segue o padrão'
                      : (
                        <>
                          prazo próprio deste tipo{' '}
                          {pode && (
                            <button type="button" className="underline"
                              onClick={() => setHerdar((h) => [...h, i.stage])}>
                              voltar ao padrão
                            </button>
                          )}
                        </>
                      )}
                  </p>
                )}
                {i.updatedByLabel && (
                  <p className="sub">
                    alterado por <strong>{i.updatedByLabel}</strong> em {data(i.updatedAt)}
                  </p>
                )}
              </Campo>
            ))}
          </div>

          {pode ? (
            <div className="mt-3">
              <button type="submit" className="botao" disabled={invalido || salvando}>
                {salvando ? 'Salvando…' : 'Salvar prazos'}
              </button>
            </div>
          ) : (
            <Nota>
              Só o administrador define os prazos: mudá-los muda o veredito de toda a operação
              de uma vez. Quem usa a Torre vê a régua para saber contra o que a linha ficou
              vermelha.
            </Nota>
          )}
        </form>
      )}
    </Painel>
  );
}
