import { useEffect, useState, type FormEvent } from 'react';
import {
  chaveDoPrazo, leituraDoAtendimento, lerPrazosDeAtendimento, salvarPrazosDeAtendimento,
  type PrazoDeAtendimento,
} from '@/api/prazoDeAtendimento';
import { Carregando, Erro, Painel } from '@/componentes/basicos';
import { Campo, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { data } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

/**
 * Uma linha do cadastro: o padrão ou a exceção de uma família.
 *
 * <p>
 * Componente de <strong>módulo</strong>, e não aninhado na tela: definido dentro dela seria um
 * tipo novo a cada render, o React remontaria o campo a cada tecla e o foco se perderia no meio
 * da digitação.
 * </p>
 */
function Linha({ item, pode, herdar, valor, avisoEmPct, aoDigitar, aoHerdar }: {
  item: PrazoDeAtendimento; pode: boolean; herdar: string[]; valor: string;
  avisoEmPct: number; aoDigitar: (v: string) => void; aoHerdar: () => void;
}) {
  const chave = chaveDoPrazo(item);
  const ehPadrao = item.family === null;
  const campoId = `matsla-${chave || 'PADRAO'}`;
  return (
    <Campo id={campoId}
      rotulo={ehPadrao ? 'Padrão' : item.family}
      dica={ehPadrao ? 'vale para a família que não definir o seu' : 'dias corridos'}>
      <input id={campoId} type="number" min={0} max={365} step={1}
        disabled={!pode} value={valor} onChange={(e) => aoDigitar(e.target.value)} />
      <p className="sub mt-1" data-testid={`leitura-${chave || 'PADRAO'}`}>
        {leituraDoAtendimento(Number(valor || 0), avisoEmPct)}
      </p>
      {/* herdada acompanha o padrão; própria é exceção da família. Sem dizer qual é qual, o
          administrador não sabe se mexer aqui muda uma família ou todas */}
      {!ehPadrao && (
        <p className="sub" data-testid={`origem-${chave}`}>
          {herdar.includes(chave) ? 'voltará a seguir o padrão'
            : item.inherited ? 'segue o padrão'
            : (
              <>
                prazo próprio desta família{' '}
                {pode && (
                  <button type="button" className="underline" onClick={aoHerdar}>
                    voltar ao padrão
                  </button>
                )}
              </>
            )}
        </p>
      )}
      {item.updatedByLabel && (
        <p className="sub">
          alterado por <strong>{item.updatedByLabel}</strong> em {data(item.updatedAt)}
        </p>
      )}
    </Campo>
  );
}

/**
 * Quanto tempo o almoxarifado tem para atender, por família de produto.
 *
 * <p>
 * A fila do almoxarifado já dizia há quantos dias cada solicitação espera; faltava alguém
 * dizer que aquilo é tempo demais — e o tempo demais <strong>depende do que se pediu</strong>:
 * luva da prateleira não tem o mesmo prazo de uma peça que o almoxarife busca em outro galpão.
 * </p>
 *
 * <p>
 * É a mesma régua de Prazos por Etapa, de propósito: o <strong>padrão</strong> vale para quem
 * não definiu o seu, a família que define <strong>para de herdar</strong>, voltar a herdar
 * <strong>apaga a exceção</strong> em vez de copiar o número, e <strong>zero desliga</strong>.
 * </p>
 */
export function PrazosDeAtendimento() {
  const { avisar } = useToast();
  const { dados, erro, carregando, recarregar } = useCarregar(lerPrazosDeAtendimento, []);
  const [dias, setDias] = useState<Record<string, string>>({});
  // as famílias que voltam ao padrão neste salvamento
  const [herdar, setHerdar] = useState<string[]>([]);
  const [salvando, setSalvando] = useState(false);

  // o valor mostrado é o editado ou, sem edição, o do servidor — derivado, não copiado num
  // efeito: copiar deixaria um quadro com o campo na tela e o número ainda fora dele
  useEffect(() => { setDias({}); setHerdar([]); }, [dados]);
  const linhas = dados?.items ?? [];
  const valorDe = (chave: string) =>
    dias[chave] ?? String(linhas.find((i) => chaveDoPrazo(i) === chave)?.maxDays ?? '');

  const pode = dados?.canEdit ?? false;
  const invalido = linhas.some((i) => {
    const v = valorDe(chaveDoPrazo(i));
    const n = Number(v);
    return v.trim() === '' || !Number.isInteger(n) || n < 0 || n > 365;
  });

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setSalvando(true);
    try {
      // Só vai ao servidor o que é decisão de alguém: o padrão, a família que já tem prazo
      // próprio e a que foi editada agora. A família **herdada e intocada fica de fora** —
      // mandá-la gravaria o número do padrão como exceção dela, e salvar a tela sem mexer em
      // nada congelaria todas as heranças de uma vez, que é o oposto do que a herança faz.
      // Família marcada para herdar também não vai como número: ela é apagada.
      const valores = linhas
        .filter((i) => {
          const c = chaveDoPrazo(i);
          if (herdar.includes(c)) return false;
          return i.family === null || !i.inherited || dias[c] !== undefined;
        })
        .map((i) => [chaveDoPrazo(i), Number(valorDe(chaveDoPrazo(i)))] as const);
      await salvarPrazosDeAtendimento(Object.fromEntries(valores), herdar);
      avisar('Prazos de atendimento atualizados. A fila do almoxarifado já cobra por eles.');
      recarregar();
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao salvar os prazos.', 'erro'); }
    finally { setSalvando(false); }
  }

  const padrao = linhas.find((i) => i.family === null);
  const familias = linhas.filter((i) => i.family !== null);
  // as props da linha, montadas aqui: o componente é de módulo, não aninhado — definido
  // dentro daqui ele seria um tipo novo a cada render, o React remontaria o campo a cada
  // tecla e o foco se perderia no meio da digitação
  const propsDa = (item: PrazoDeAtendimento) => ({
    item, pode, herdar,
    valor: valorDe(chaveDoPrazo(item)),
    avisoEmPct: dados?.warnAtPercent ?? 80,
    aoDigitar: (v: string) => setDias((d) => ({ ...d, [chaveDoPrazo(item)]: v })),
    aoHerdar: () => setHerdar((h) => [...h, chaveDoPrazo(item)]),
  });

  return (
    <Painel titulo="Prazos do Almoxarifado">
      <Nota>
        Quanto tempo o almoxarifado tem para atender, contado da{' '}
        <strong>liberação do Nível 1</strong> — não da criação da solicitação, senão a espera
        pela aprovação do centro cairia na conta de quem não teve culpa. A solicitação tem itens
        de várias famílias e responde pelo prazo <strong>mais curto</strong> deles: valer o mais
        longo deixaria o item de dois dias parado dez. <strong>Zero desliga</strong> a cobrança
        da família, e a família em zero sai da conta em vez de zerar a solicitação inteira. O
        prazo <strong>mede e expõe, nunca bloqueia</strong>: estourado aparece na fila, mas não
        impede atender.
      </Nota>
      <Nota>
        Não confundir com os <strong>prazos-meta da família</strong>, no cadastro de Famílias de
        Produtos: aqueles são do processo de <strong>compra</strong> (solicitação → cotação →
        aprovação → O.C. → entrega). Este é o material que já está em estoque e sai pela porta
        do almoxarifado, sem compra nenhuma.
      </Nota>

      {erro && <Erro>{erro}</Erro>}
      {carregando && !dados && <Carregando />}

      {dados && (
        <form onSubmit={enviar} data-testid="form-prazos-atendimento">
          {padrao && (
            <div className="mb-4 max-w-[360px]">
              <Linha {...propsDa(padrao)} />
            </div>
          )}

          {familias.length > 0 ? (
            <>
              <p className="rotulo mb-1">Exceções por família</p>
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
                {familias.map((i) => <Linha key={i.family} {...propsDa(i)} />)}
              </div>
            </>
          ) : (
            <Nota>
              Nenhuma família está marcada como de almoxarifado em Famílias de Produtos, então
              só há o padrão aqui. É no cadastro de famílias que se diz quais entram em
              Solicitar Material.
            </Nota>
          )}

          {pode ? (
            <div className="mt-3">
              <button type="submit" className="botao" disabled={invalido || salvando}>
                {salvando ? 'Salvando…' : 'Salvar prazos'}
              </button>
            </div>
          ) : (
            <Nota>
              Só o administrador define estes prazos: a régua vale para a operação inteira, e
              mudá-la muda o veredito de toda a fila de uma vez. Quem trabalha na fila vê a
              régua para saber contra o que a linha ficou vermelha.
            </Nota>
          )}
        </form>
      )}
    </Painel>
  );
}
