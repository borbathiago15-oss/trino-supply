import { useEffect, useState, type FormEvent } from 'react';
import {
  CHAVES_DE_PESO, lerPesosDoScore, salvarPesosDoScore, somaDosPesos,
  type ChaveDePeso,
} from '@/api/pesosDoScore';
import { Carregando, Erro, Painel } from '@/componentes/basicos';
import { Campo, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const vazio: Record<ChaveDePeso, string> = { price: '', delivery: '', payment: '', otif: '', risk: '' };

/**
 * A régua do score multicritério, como a empresa a define.
 *
 * <p>
 * Os pesos viviam fixos no C#: passar a valorizar entrega acima de preço exigia alterar
 * código e implantar. A régua era da compra e estava trancada com o programador.
 * </p>
 *
 * <p>
 * A soma aparece enquanto se digita, e o botão só libera em 100. Não é rigor de formulário:
 * a cotação publica "Preço 40% · Entrega 20% · …" como repartição de um todo, e peso que
 * não fecha faria a explicação mentir sobre a própria conta — quem configurasse 50/30/10/20/10
 * acharia estar dando 50% ao preço quando estaria dando 42.
 * </p>
 */
export function PesosDoScore() {
  const { avisar } = useToast();
  const { dados, erro, carregando, recarregar } = useCarregar(lerPesosDoScore, []);
  const [pesos, setPesos] = useState<Record<ChaveDePeso, string>>(vazio);
  const [salvando, setSalvando] = useState(false);

  useEffect(() => {
    if (!dados) return;
    setPesos({
      price: String(dados.weights.price), delivery: String(dados.weights.delivery),
      payment: String(dados.weights.payment), otif: String(dados.weights.otif),
      risk: String(dados.weights.risk),
    });
  }, [dados]);

  const soma = somaDosPesos(pesos);
  const fecha = soma === 100;
  const pode = dados?.canEdit ?? false;

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      await salvarPesosDoScore({
        price: Number(pesos.price), delivery: Number(pesos.delivery),
        payment: Number(pesos.payment), otif: Number(pesos.otif), risk: Number(pesos.risk),
      });
      avisar('Pesos do score atualizados. As próximas comparações já usam esta régua.');
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao salvar os pesos.', 'erro'); }
    finally { setSalvando(false); }
  }

  return (
    <Painel titulo="Pesos do Score da Cotação">
      <Nota>
        É com esta régua que o sistema compara propostas na tela do processo. Ela{' '}
        <strong>informa e não decide</strong>: o score continua sem bloquear escolha nenhuma,
        e a justificativa do comprador segue obrigatória. Os pesos precisam somar{' '}
        <strong>100</strong> — é o que faz o percentual que a cotação mostra ser o percentual
        que a conta usa. <strong>Peso 0 desliga o critério</strong>, que é o jeito honesto de
        dizer que ali aquilo não importa.
      </Nota>

      {erro && <Erro>{erro}</Erro>}
      {carregando && !dados && <Carregando />}

      {dados && (
        <form onSubmit={enviar} data-testid="form-pesos-score">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            {dados.weights.criteria.map((c) => (
              <Campo key={c.code} id={`peso-${c.code}`} rotulo={c.label} dica={c.help}>
                <input
                  id={`peso-${c.code}`} type="number" min={0} max={100} step={1}
                  disabled={!pode}
                  value={pesos[c.code as ChaveDePeso] ?? ''}
                  onChange={(e) => setPesos((p) => ({ ...p, [c.code]: e.target.value }))}
                />
              </Campo>
            ))}
          </div>

          <p className="mt-3" data-testid="soma-dos-pesos">
            Soma:{' '}
            <strong className={fecha ? 'text-ok' : 'text-perigo'}>{soma}</strong>
            {!fecha && (
              <span className="sub">
                {' '}— falta{soma > 100 ? 'm sobrando ' : ' '}{Math.abs(100 - soma)} para fechar 100.
              </span>
            )}
          </p>

          {CHAVES_DE_PESO.every((k) => Number(pesos[k]) === 0) && (
            <Nota>Com todos os critérios em zero não sobra régua nenhuma — algum precisa de peso.</Nota>
          )}

          {pode && (
            <div className="mt-3">
              <button type="submit" className="botao" disabled={!fecha || salvando}>
                {salvando ? 'Salvando…' : 'Salvar pesos'}
              </button>
            </div>
          )}

          {!pode && (
            <Nota>
              Só o administrador altera esta régua: mudá-la muda a ordem de todas as
              comparações da empresa de uma vez.
            </Nota>
          )}

          {dados.weights.updatedByLabel && (
            <p className="sub mt-2">
              Última alteração por <strong>{dados.weights.updatedByLabel}</strong> em{' '}
              {data(dados.weights.updatedAt)}.
            </p>
          )}
        </form>
      )}
    </Painel>
  );
}
