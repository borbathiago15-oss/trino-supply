import { useState, type ChangeEvent } from 'react';
import {
  GRADES_DE_TAMANHO, importarPlanilha, type LinhaImportacao, type ResultadoImportacao,
  type SituacaoLinha, type TipoDeProduto,
} from '@/api/catalogo';
import { Badge, Painel } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';

const MARCA: Record<SituacaoLinha, { rotulo: string; classe: string }> = {
  NOVO: { rotulo: 'NOVO', classe: 'bg-ok-fundo text-ok' },
  DUPLICADO: { rotulo: 'JÁ EXISTE', classe: 'bg-slate-100 text-slate-600' },
  ERRO: { rotulo: 'ERRO', classe: 'bg-perigo-fundo text-perigo' },
};

/** As linhas com problema vêm primeiro: são as que o usuário precisa corrigir. */
export const separarLinhas = (linhas: LinhaImportacao[]) => ({
  problemas: linhas.filter((r) => r.status !== 'NOVO'),
  amostra: linhas.filter((r) => r.status === 'NOVO').slice(0, 8),
});

export function PainelImportacao({ familias, tipos, aoImportar, aoFechar }: {
  familias: string[];
  tipos: TipoDeProduto[];
  aoImportar: () => void;
  aoFechar: () => void;
}) {
  const { avisar } = useToast();
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [familia, setFamilia] = useState('');
  const [tipo, setTipo] = useState('');
  const [unidade, setUnidade] = useState('');
  const [tamanhos, setTamanhos] = useState('');
  const [ocupado, setOcupado] = useState(false);
  const [resultado, setResultado] = useState<{ dados: ResultadoImportacao; gravado: boolean } | null>(null);

  async function enviar(commit: boolean) {
    if (!arquivo) { avisar('Escolha a planilha primeiro.', 'erro'); return; }
    if (!familia) { avisar('Escolha a família dos produtos.', 'erro'); return; }
    setOcupado(true);
    try {
      const dados = await importarPlanilha({
        arquivo, family: familia, productType: tipo || null,
        unit: unidade || null, sizes: tamanhos || null, commit,
      });
      setResultado({ dados, gravado: commit });
      if (commit) {
        avisar(`${dados.toCreate} produto(s) importado(s).`);
        setArquivo(null);
        aoImportar();
      }
    } catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao ler a planilha.', 'erro'); }
    finally { setOcupado(false); }
  }

  const { problemas, amostra } = resultado ? separarLinhas(resultado.dados.rows) : { problemas: [], amostra: [] };

  return (
    <Painel id="importacao" titulo="Importar produtos por planilha"
      acoes={<button type="button" className="botao-secundario" onClick={aoFechar}>Fechar</button>}>
      <Nota>
        A planilha precisa ter as colunas <strong>Produto</strong> (código) e <strong>Descrição</strong> — .xlsx ou .csv.
        Família, tipo e unidade valem para o arquivo inteiro. A prévia mostra o que será criado antes de confirmar.
      </Nota>

      <Grade2 className="mt-3">
        <Campo id="imp-arquivo" rotulo="Planilha">
          <input id="imp-arquivo" type="file" accept=".xlsx,.csv,.txt"
            onChange={(ev: ChangeEvent<HTMLInputElement>) => { setArquivo(ev.target.files?.[0] ?? null); setResultado(null); }} />
        </Campo>
        <Campo id="imp-familia" rotulo="Família">
          <select id="imp-familia" value={familia} onChange={(e) => setFamilia(e.target.value)}>
            <option value="">Selecione a família…</option>
            {familias.map((f) => <option key={f} value={f}>{f}</option>)}
          </select>
        </Campo>
      </Grade2>
      <Grade2 className="mt-3">
        <Campo id="imp-tipo" rotulo="Tipo de produto">
          <select id="imp-tipo" value={tipo} onChange={(e) => setTipo(e.target.value)}>
            <option value="">Sem tipo definido</option>
            {tipos.map((t) => <option key={t.key} value={t.key}>{t.label}</option>)}
          </select>
        </Campo>
        <Campo id="imp-unidade" rotulo="Unidade padrão">
          <input id="imp-unidade" placeholder="UN" value={unidade} onChange={(e) => setUnidade(e.target.value)} />
        </Campo>
      </Grade2>

      <Campo id="imp-tamanhos" className="mt-3" rotulo="Grade de tamanhos"
        dica="(EPI e Fardamento: cada tamanho vira um item, ex.: 12003-P)">
        <div className="mb-1.5 flex flex-wrap gap-2">
          <button type="button" className="botao-secundario" onClick={() => setTamanhos(GRADES_DE_TAMANHO.letras)}>Letras (PP…XXG)</button>
          <button type="button" className="botao-secundario" onClick={() => setTamanhos(GRADES_DE_TAMANHO.numeros)}>Numéricos (34…46)</button>
          <button type="button" className="botao-secundario" onClick={() => setTamanhos(GRADES_DE_TAMANHO.limpar)}>Sem tamanhos</button>
        </div>
        <input id="imp-tamanhos" placeholder="deixe vazio para não desdobrar — ou informe: P, M, G, GG"
          value={tamanhos} onChange={(e) => setTamanhos(e.target.value)} />
      </Campo>

      <div className="mt-4 flex flex-wrap gap-2">
        <button type="button" className="botao" disabled={ocupado} onClick={() => enviar(false)}>Pré-visualizar</button>
        {resultado && !resultado.gravado && resultado.dados.toCreate > 0 && (
          <button type="button" className="botao" disabled={ocupado} onClick={() => enviar(true)}>Confirmar importação</button>
        )}
      </div>

      {resultado && (
        <div className="mt-4" data-testid="resultado-importacao">
          <p className="sub">
            <strong>{resultado.dados.fileName}</strong> · {resultado.dados.totalLines} linha(s) lida(s) —{' '}
            <strong>{resultado.dados.toCreate}</strong> produto(s) {resultado.gravado ? 'importado(s)' : 'a criar'},{' '}
            {resultado.dados.duplicates} já existente(s), {resultado.dados.errors} com erro.
          </p>
          {resultado.dados.warnings?.map((w) => (
            <p key={w} className="mt-1 rounded-lg bg-aviso-fundo px-3 py-2 text-[13px] text-aviso">{w}</p>
          ))}

          {amostra.length > 0 && (
            <div className="mt-3 overflow-x-auto">
              <table>
                <thead><tr><th>Código</th><th>Descrição</th><th>Tam.</th><th>Situação</th></tr></thead>
                <tbody>
                  {amostra.map((r) => (
                    <tr key={r.line}>
                      <td>{r.code}</td><td>{r.description}</td><td>{r.size || '—'}</td>
                      <td><Badge classe={MARCA[r.status].classe}>{MARCA[r.status].rotulo}</Badge></td>
                    </tr>
                  ))}
                  {resultado.dados.toCreate > amostra.length && (
                    <tr><td colSpan={4} className="sub">…e mais {resultado.dados.toCreate - amostra.length} produto(s).</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          )}

          {problemas.length > 0 && (
            <>
              <h3 className="mb-1 mt-4 text-[14px] font-bold">Linhas não importadas</h3>
              <div className="overflow-x-auto">
                <table data-testid="linhas-com-problema">
                  <thead><tr><th>Linha</th><th>Código</th><th>Situação</th><th>Motivo</th></tr></thead>
                  <tbody>
                    {problemas.slice(0, 30).map((r) => (
                      <tr key={r.line}>
                        <td>{r.line}</td><td>{r.code}</td>
                        <td><Badge classe={MARCA[r.status].classe}>{MARCA[r.status].rotulo}</Badge></td>
                        <td className="sub">{r.message ?? ''}</td>
                      </tr>
                    ))}
                    {problemas.length > 30 && (
                      <tr><td colSpan={4} className="sub">…e mais {problemas.length - 30} linha(s).</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </div>
      )}
    </Painel>
  );
}
