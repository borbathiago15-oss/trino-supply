import { useState } from 'react';
import { Campo, Grade2, Nota } from '@/componentes/formulario';

/**
 * Os formulários das oito ferramentas.
 *
 * <p>
 * Eles só <b>coletam</b>. Quem ordena o Pareto, marca a causa vital e decide se o GUT elege
 * alguém é o servidor — repetir a régua aqui daria dois donos para a mesma conta, e a tela
 * que mostrasse o número errado seria a que o usuário acreditou.
 * </p>
 *
 * <p>
 * Cada um lê e escreve o mesmo JSON que o Trino Intelligence usa, então uma análise de lá
 * abre aqui sem conversão.
 * </p>
 */

/** O JSON gravado, já lido — objeto vazio quando ainda não há nada ou o texto não é JSON. */
export function lerDados(texto: string | null | undefined): Record<string, unknown> {
  if (!texto?.trim()) return {};
  try {
    const v: unknown = JSON.parse(texto);
    return v && typeof v === 'object' && !Array.isArray(v) ? (v as Record<string, unknown>) : {};
  } catch { return {}; }
}

const textos = (v: unknown): string[] =>
  Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : [];

const objetos = (v: unknown): Record<string, unknown>[] =>
  Array.isArray(v) ? v.filter((x): x is Record<string, unknown> =>
    !!x && typeof x === 'object' && !Array.isArray(x)) : [];

const texto = (o: Record<string, unknown>, ...campos: string[]): string => {
  for (const c of campos) if (typeof o[c] === 'string') return o[c] as string;
  return '';
};

const numero = (o: Record<string, unknown>, campo: string): number => {
  const v = o[campo];
  if (typeof v === 'number') return v;
  if (typeof v === 'string' && v.trim() !== '') return Number(v.replace(',', '.')) || 0;
  return 0;
};

/**
 * Uma lista editada como linhas: é como se digita de verdade, uma por linha.
 *
 * <p>
 * O texto fica <b>aqui</b>, e não é remontado da lista a cada tecla. Remontando, a linha
 * vazia que acabou de nascer era filtrada antes de receber a primeira letra — e não havia
 * como escrever a segunda linha.
 * </p>
 */
function Linhas({ id, rotulo, dica, valor, aoMudar }: {
  id: string; rotulo: string; dica?: string; valor: string[]; aoMudar: (v: string[]) => void;
}) {
  const [texto, setTexto] = useState(valor.join('\n'));
  return (
    <Campo id={id} rotulo={rotulo} dica={dica ?? '(uma por linha)'}>
      <textarea id={id} rows={3} value={texto}
        onChange={(e) => {
          setTexto(e.target.value);
          aoMudar(e.target.value.split('\n').map((l) => l.trim()).filter(Boolean));
        }} />
    </Campo>
  );
}

export interface PropsDoFormulario {
  dados: Record<string, unknown>;
  aoMudar: (dados: Record<string, unknown>) => void;
}

// ---- 5 Porquês -------------------------------------------------------------

function CincoPorques({ dados, aoMudar }: PropsDoFormulario) {
  const set = (k: string, v: string) => aoMudar({ ...dados, [k]: v });
  return (
    <>
      <Campo id="fp-problema" rotulo="Problema">
        <input id="fp-problema" value={texto(dados, 'problema', 'problem')}
          onChange={(e) => set('problema', e.target.value)} />
      </Campo>
      {/* a escada recua a cada degrau: é o desenho da ferramenta, e ele ajuda a
          perceber quando a resposta parou de descer */}
      {[1, 2, 3, 4, 5].map((n) => (
        <div key={n} className="mt-3" style={{ marginLeft: (n - 1) * 14 }}>
          <Grade2>
            <Campo id={`fp-p${n}`} rotulo={`${n}º por quê?`}>
              <input id={`fp-p${n}`} value={texto(dados, `porque${n}`, `w${n}_why`)}
                onChange={(e) => set(`porque${n}`, e.target.value)} />
            </Campo>
            <Campo id={`fp-r${n}`} rotulo="Resposta">
              <input id={`fp-r${n}`} value={texto(dados, `resposta${n}`, `w${n}_ans`)}
                onChange={(e) => set(`resposta${n}`, e.target.value)} />
            </Campo>
          </Grade2>
        </div>
      ))}
      <Campo id="fp-raiz" rotulo="Causa raiz" className="mt-3"
        dica="ela preenche a causa raiz do ciclo, se ninguém escreveu outra">
        <input id="fp-raiz" value={texto(dados, 'causa_raiz', 'root_cause')}
          onChange={(e) => set('causa_raiz', e.target.value)} />
      </Campo>
    </>
  );
}

// ---- Ishikawa --------------------------------------------------------------

const SEIS_EMES: [string, string][] = [
  ['maquina', 'Máquina'], ['metodo', 'Método'], ['mao_de_obra', 'Mão de obra'],
  ['material', 'Material'], ['medicao', 'Medição'], ['meio_ambiente', 'Meio ambiente'],
];

function Ishikawa({ dados, aoMudar }: PropsDoFormulario) {
  return (
    <>
      <Campo id="fi-efeito" rotulo="Efeito" dica="o problema que as causas produzem">
        <input id="fi-efeito" value={texto(dados, 'efeito', 'effect')}
          onChange={(e) => aoMudar({ ...dados, efeito: e.target.value })} />
      </Campo>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        {SEIS_EMES.map(([chave, rotulo]) => (
          <Linhas key={chave} id={`fi-${chave}`} rotulo={rotulo} valor={textos(dados[chave])}
            aoMudar={(v) => aoMudar({ ...dados, [chave]: v })} />
        ))}
      </div>
      <Nota>O espinha-de-peixe levanta as causas; quem prioriza é o Pareto ou o GUT.</Nota>
    </>
  );
}

// ---- Pareto ----------------------------------------------------------------

function Pareto({ dados, aoMudar }: PropsDoFormulario) {
  const itens = objetos(dados.itens ?? dados.items);
  const trocar = (i: number, campo: string, v: string) => {
    const novo = itens.map((x, j) => (j === i ? { ...x, [campo]: campo === 'valor' ? Number(v.replace(',', '.')) || 0 : v } : x));
    aoMudar({ ...dados, itens: novo });
  };
  return (
    <>
      <table data-testid="form-pareto">
        <thead><tr><th>Causa</th><th className="w-32">Valor</th><th /></tr></thead>
        <tbody>
          {itens.map((it, i) => (
            <tr key={i}>
              <td><input aria-label={`Causa ${i + 1}`} value={texto(it, 'causa', 'problema')}
                onChange={(e) => trocar(i, 'causa', e.target.value)} /></td>
              <td><input aria-label={`Valor ${i + 1}`} inputMode="decimal" value={String(numero(it, 'valor'))}
                onChange={(e) => trocar(i, 'valor', e.target.value)} /></td>
              <td>
                <button type="button" className="botao-perigo"
                  onClick={() => aoMudar({ ...dados, itens: itens.filter((_, j) => j !== i) })}>
                  Remover
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <button type="button" className="botao-secundario mt-2"
        onClick={() => aoMudar({ ...dados, itens: [...itens, { causa: '', valor: 0 }] })}>
        Acrescentar causa
      </button>
      {/* a ordem e a faixa vital saem do servidor: repeti-las aqui daria dois donos
          para a mesma régua, e a tela mostraria a conta errada com cara de certa */}
      <Nota>A ordem, o percentual e a faixa dos 80% aparecem depois de salvar — quem calcula é o servidor.</Nota>
    </>
  );
}

// ---- GUT -------------------------------------------------------------------

function Gut({ dados, aoMudar }: PropsDoFormulario) {
  const itens = objetos(dados.itens ?? dados.items);
  const trocar = (i: number, campo: string, v: string) => {
    const novo = itens.map((x, j) => (j === i ? { ...x, [campo]: campo === 'problema' ? v : Number(v) || 0 } : x));
    aoMudar({ ...dados, itens: novo });
  };
  return (
    <>
      <table data-testid="form-gut">
        <thead>
          <tr><th>Problema</th><th className="w-20">G</th><th className="w-20">U</th><th className="w-20">T</th><th className="w-16">G×U×T</th><th /></tr>
        </thead>
        <tbody>
          {itens.map((it, i) => {
            const produto = numero(it, 'g') * numero(it, 'u') * numero(it, 't');
            return (
              <tr key={i}>
                <td><input aria-label={`Problema ${i + 1}`} value={texto(it, 'problema', 'causa')}
                  onChange={(e) => trocar(i, 'problema', e.target.value)} /></td>
                {(['g', 'u', 't'] as const).map((c) => (
                  <td key={c}>
                    <select aria-label={`${c.toUpperCase()} ${i + 1}`} value={String(numero(it, c) || '')}
                      onChange={(e) => trocar(i, c, e.target.value)}>
                      <option value="">—</option>
                      {[1, 2, 3, 4, 5].map((n) => <option key={n} value={n}>{n}</option>)}
                    </select>
                  </td>
                ))}
                {/* o produto é multiplicação, e por isso pode aparecer aqui. Quem é vital
                    não: isso depende de ordenar e do piso de 27, e é do servidor */}
                <td className="tabular-nums font-semibold">{produto || '—'}</td>
                <td>
                  <button type="button" className="botao-perigo"
                    onClick={() => aoMudar({ ...dados, itens: itens.filter((_, j) => j !== i) })}>
                    Remover
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <button type="button" className="botao-secundario mt-2"
        onClick={() => aoMudar({ ...dados, itens: [...itens, { problema: '', g: 3, u: 3, t: 3 }] })}>
        Acrescentar problema
      </button>
      <Nota>Notas de 1 a 5. A matriz só elege alguém se o maior produto chegar a 27.</Nota>
    </>
  );
}

// ---- Brainstorming ---------------------------------------------------------

function Brainstorming({ dados, aoMudar }: PropsDoFormulario) {
  return (
    <Linhas id="fb-ideias" rotulo="Ideias" valor={textos(dados.ideias ?? dados.ideas)}
      aoMudar={(v) => aoMudar({ ...dados, ideias: v })} />
  );
}

// ---- 5W2H ------------------------------------------------------------------

const COLUNAS_5W2H: [string, string][] = [
  ['oque', 'O quê'], ['porque', 'Por quê'], ['onde', 'Onde'], ['quando', 'Quando'],
  ['quem', 'Quem'], ['como', 'Como'], ['quanto', 'Quanto'],
];

const ALIAS_5W2H: Record<string, string> = {
  oque: 'what', porque: 'why', onde: 'where', quando: 'when',
  quem: 'who', como: 'how', quanto: 'how_much',
};

function CincoWDoisH({ dados, aoMudar }: PropsDoFormulario) {
  const linhas = objetos(dados.linhas ?? dados.rows ?? dados.itens);
  const trocar = (i: number, campo: string, v: string) =>
    aoMudar({ ...dados, linhas: linhas.map((x, j) => (j === i ? { ...x, [campo]: v } : x)) });
  return (
    <>
      <div className="overflow-x-auto">
        <table data-testid="form-5w2h">
          <thead><tr>{COLUNAS_5W2H.map(([, r]) => <th key={r}>{r}</th>)}<th /></tr></thead>
          <tbody>
            {linhas.map((l, i) => (
              <tr key={i}>
                {COLUNAS_5W2H.map(([chave, rotulo]) => (
                  <td key={chave}>
                    <input aria-label={`${rotulo} ${i + 1}`} value={texto(l, chave, ALIAS_5W2H[chave])}
                      onChange={(e) => trocar(i, chave, e.target.value)} />
                  </td>
                ))}
                <td>
                  <button type="button" className="botao-perigo"
                    onClick={() => aoMudar({ ...dados, linhas: linhas.filter((_, j) => j !== i) })}>
                    Remover
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <button type="button" className="botao-secundario mt-2"
        onClick={() => aoMudar({ ...dados, linhas: [...linhas, { oque: '' }] })}>
        Acrescentar linha
      </button>
      <Nota>Aqui é o rascunho. Quando virar trabalho com dono e prazo, a linha vira ação no Plano de Ação.</Nota>
    </>
  );
}

// ---- Kaizen ----------------------------------------------------------------

function Kaizen({ dados, aoMudar }: PropsDoFormulario) {
  return (
    <>
      <Grade2>
        <Campo id="fk-antes" rotulo="Antes">
          <textarea id="fk-antes" rows={3} value={texto(dados, 'antes', 'situation_before')}
            onChange={(e) => aoMudar({ ...dados, antes: e.target.value })} />
        </Campo>
        <Campo id="fk-depois" rotulo="Depois">
          <textarea id="fk-depois" rows={3} value={texto(dados, 'depois', 'situation_after')}
            onChange={(e) => aoMudar({ ...dados, depois: e.target.value })} />
        </Campo>
      </Grade2>
      <div className="mt-3">
        <Linhas id="fk-melhorias" rotulo="Melhorias implementadas"
          valor={textos(dados.melhorias ?? dados.improvements)}
          aoMudar={(v) => aoMudar({ ...dados, melhorias: v })} />
      </div>
      <Campo id="fk-resultados" rotulo="Resultados" className="mt-3">
        <textarea id="fk-resultados" rows={2} value={texto(dados, 'resultados', 'results')}
          onChange={(e) => aoMudar({ ...dados, resultados: e.target.value })} />
      </Campo>
    </>
  );
}

// ---- Fluxograma ------------------------------------------------------------

function Fluxograma({ dados, aoMudar }: PropsDoFormulario) {
  return (
    <>
      <div className="grid gap-3 sm:grid-cols-2">
        <Linhas id="ff-atual" rotulo="Fluxo atual" dica="(uma etapa por linha)"
          valor={textos(dados.atual ?? dados.current)}
          aoMudar={(v) => aoMudar({ ...dados, atual: v })} />
        <Linhas id="ff-proposto" rotulo="Fluxo proposto" dica="(uma etapa por linha)"
          valor={textos(dados.proposto ?? dados.proposed)}
          aoMudar={(v) => aoMudar({ ...dados, proposto: v })} />
      </div>
      <Nota>A comparação é a informação: um fluxo sozinho não diz o que muda.</Nota>
    </>
  );
}

// ---- o que a tela usa ------------------------------------------------------

/** O formulário de cada ferramenta. Chave desconhecida cai no JSON cru, que é honesto. */
export function FormularioDaFerramenta({ chave, dados, aoMudar }:
  { chave: string } & PropsDoFormulario) {
  switch (chave) {
    case 'CINCO_PORQUES': return <CincoPorques dados={dados} aoMudar={aoMudar} />;
    case 'ISHIKAWA': return <Ishikawa dados={dados} aoMudar={aoMudar} />;
    case 'PARETO': return <Pareto dados={dados} aoMudar={aoMudar} />;
    case 'GUT': return <Gut dados={dados} aoMudar={aoMudar} />;
    case 'BRAINSTORMING': return <Brainstorming dados={dados} aoMudar={aoMudar} />;
    case 'CINCO_W_DOIS_H': return <CincoWDoisH dados={dados} aoMudar={aoMudar} />;
    case 'KAIZEN': return <Kaizen dados={dados} aoMudar={aoMudar} />;
    case 'FLUXOGRAMA': return <Fluxograma dados={dados} aoMudar={aoMudar} />;
    default: return <JsonCru dados={dados} aoMudar={aoMudar} />;
  }
}

/**
 * A saída de emergência: ferramenta que a tela ainda não desenha continua editável pelo JSON,
 * em vez de virar um campo que não abre.
 */
function JsonCru({ dados, aoMudar }: PropsDoFormulario) {
  const [texto, setTexto] = useState(JSON.stringify(dados, null, 2));
  return (
    <Campo id="fj-json" rotulo="Dados da ferramenta" dica="esta ferramenta ainda não tem formulário próprio">
      <textarea id="fj-json" rows={6} className="font-mono text-[12px]" value={texto}
        onChange={(e) => {
          setTexto(e.target.value);
          try { aoMudar(JSON.parse(e.target.value) as Record<string, unknown>); } catch { /* texto em edição */ }
        }} />
    </Campo>
  );
}
