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
  const { dados, erro, carregando, recarregar } = useCarregar(lerPrazosDasEtapas, []);
  const [dias, setDias] = useState<Record<string, string>>({});
  const [salvando, setSalvando] = useState(false);

  useEffect(() => {
    if (!dados) return;
    setDias(Object.fromEntries(dados.items.map((i) => [i.stage, String(i.maxDays)])));
  }, [dados]);

  const pode = dados?.canEdit ?? false;
  const invalido = Object.values(dias).some((v) => {
    const n = Number(v);
    return v.trim() === '' || !Number.isInteger(n) || n < 0 || n > 365;
  });

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      await salvarPrazosDasEtapas(
        Object.fromEntries(Object.entries(dias).map(([etapa, v]) => [etapa, Number(v)])));
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
                  value={dias[i.stage] ?? ''}
                  onChange={(e) => setDias((d) => ({ ...d, [i.stage]: e.target.value }))} />
                <p className="sub mt-1" data-testid={`leitura-${i.stage}`}>
                  {leituraDoPrazo(Number(dias[i.stage] ?? 0), dados.warnAtPercent)}
                </p>
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
