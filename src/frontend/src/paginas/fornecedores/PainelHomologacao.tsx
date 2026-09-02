import { useState, type ChangeEvent } from 'react';
import { abrirBlob } from '@/api/cliente';
import { baixarDocumento } from '@/api/documentos';
import {
  anexarDocumento, removerDocumento, ROTULO_DOCUMENTO, ROTULO_HOMOLOGACAO, salvarHomologacao,
  type Fornecedor, type SituacaoHomologacao, type TipoDocumento,
} from '@/api/fornecedores';
import { Badge, Painel, Vazio } from '@/componentes/basicos';
import { Campo, Grade2, Nota } from '@/componentes/formulario';
import { Confirmacao } from '@/componentes/Dialogo';
import { useToast } from '@/componentes/Toast';
import { data } from '@/util/formato';

const SITUACOES = Object.keys(ROTULO_HOMOLOGACAO) as SituacaoHomologacao[];
const TIPOS = Object.keys(ROTULO_DOCUMENTO) as TipoDocumento[];
const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/** Situação de uma certidão: vencida, vencendo em até 30 dias, válida ou sem prazo. */
export function situacaoDocumento(d: { expired: boolean; expiringDays: number | null; validUntil: string | null }) {
  if (d.expired) return { rotulo: 'VENCIDA', classe: 'bg-perigo-fundo text-perigo' };
  if (d.expiringDays != null && d.expiringDays <= 30)
    return { rotulo: `vence em ${d.expiringDays}d`, classe: 'bg-aviso-fundo text-aviso' };
  if (d.validUntil) return { rotulo: 'VÁLIDA', classe: 'bg-ok-fundo text-ok' };
  return null;
}

export function PainelHomologacao({ fornecedor, aoSalvar, aoFechar }:
  { fornecedor: Fornecedor; aoSalvar: () => void; aoFechar: () => void }) {
  const { avisar } = useToast();
  const [situacao, setSituacao] = useState<SituacaoHomologacao>(fornecedor.homologationStatus ?? 'HOMOLOGADO');
  const [tipo, setTipo] = useState<TipoDocumento>('CND_FEDERAL');
  const [validade, setValidade] = useState('');
  const [rotulo, setRotulo] = useState('');
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [aRemover, setARemover] = useState<{ id: string; fileName: string } | null>(null);

  async function salvarSituacao() {
    setOcupado(true);
    try {
      await salvarHomologacao(fornecedor.id, situacao);
      avisar('Situação de homologação salva.');
      aoSalvar();
    } catch (e) { avisar(mensagem(e, 'Falha ao salvar a situação.'), 'erro'); }
    finally { setOcupado(false); }
  }

  async function anexar() {
    if (!arquivo) { avisar('Escolha o arquivo da certidão.', 'erro'); return; }
    setOcupado(true);
    try {
      await anexarDocumento(fornecedor.id, arquivo, tipo, validade, rotulo);
      setArquivo(null); setValidade(''); setRotulo('');
      avisar('Documento anexado.');
      aoSalvar();
    } catch (e) { avisar(mensagem(e, 'Falha ao anexar o documento.'), 'erro'); }
    finally { setOcupado(false); }
  }

  async function remover(docId: string) {
    setARemover(null);
    try {
      await removerDocumento(fornecedor.id, docId);
      avisar('Documento removido.');
      aoSalvar();
    } catch (e) { avisar(mensagem(e, 'Falha ao remover o documento.'), 'erro'); }
  }

  async function abrir(documentId: string) {
    try { abrirBlob(await baixarDocumento(documentId)); }
    catch (e) { avisar(mensagem(e, 'Falha ao baixar o documento.'), 'erro'); }
  }

  return (
    <Painel id="homologacao" titulo={`Homologação — ${fornecedor.legalName}`}
      acoes={<button type="button" className="botao-secundario" onClick={aoFechar}>Fechar</button>}>
      <Nota>
        Prospect e em homologação participam de cotações e enviam propostas, mas <strong>só homologado pode ser
        escolhido vencedor</strong>. Certidão vencida restringe o fornecedor automaticamente; bloqueado não recebe convite.
      </Nota>

      <Grade2 className="mt-3">
        <Campo id="hom-situacao" rotulo="Situação da homologação" dica="(gestor de suprimentos)">
          <select id="hom-situacao" value={situacao} onChange={(ev) => setSituacao(ev.target.value as SituacaoHomologacao)}>
            {SITUACOES.map((s) => <option key={s} value={s}>{ROTULO_HOMOLOGACAO[s].rotulo}</option>)}
          </select>
        </Campo>
        <div className="flex items-end">
          <button type="button" className="botao w-full" disabled={ocupado} onClick={salvarSituacao}>Salvar situação</button>
        </div>
      </Grade2>

      <h3 className="mb-2 mt-5 text-[14px] font-bold">Certidões e documentos</h3>
      {fornecedor.documents.length ? (
        <div className="overflow-x-auto">
          <table data-testid="tabela-documentos">
            <thead><tr><th>Documento</th><th>Arquivo</th><th>Válida até</th><th>Situação</th><th></th></tr></thead>
            <tbody>
              {fornecedor.documents.map((d) => {
                const marca = situacaoDocumento(d);
                return (
                  <tr key={d.id} data-documento={d.id}>
                    <td>{ROTULO_DOCUMENTO[d.type] ?? d.type}{d.label && <div className="sub">{d.label}</div>}</td>
                    <td><button type="button" className="text-marca underline" onClick={() => abrir(d.documentId)}>{d.fileName}</button></td>
                    <td>{d.validUntil ? data(d.validUntil) : <span className="sub">sem validade</span>}</td>
                    <td>{marca ? <Badge classe={marca.classe}>{marca.rotulo}</Badge> : '—'}</td>
                    <td className="whitespace-nowrap">
                      <button type="button" className="botao-perigo !py-1.5"
                        onClick={() => setARemover({ id: d.id, fileName: d.fileName })}>Remover</button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : <Vazio>Nenhuma certidão anexada. Sem certidões cadastradas o fornecedor não é restringido automaticamente.</Vazio>}

      <Grade2 className="mt-4">
        <Campo id="doc-tipo" rotulo="Tipo">
          <select id="doc-tipo" value={tipo} onChange={(ev) => setTipo(ev.target.value as TipoDocumento)}>
            {TIPOS.map((t) => <option key={t} value={t}>{ROTULO_DOCUMENTO[t]}</option>)}
          </select>
        </Campo>
        <Grade2>
          <Campo id="doc-validade" rotulo="Válida até">
            <input id="doc-validade" type="date" value={validade} onChange={(ev) => setValidade(ev.target.value)} />
          </Campo>
          <Campo id="doc-arquivo" rotulo="Arquivo">
            <input id="doc-arquivo" type="file" accept=".pdf,.png,.jpg,.jpeg,.docx"
              onChange={(ev: ChangeEvent<HTMLInputElement>) => setArquivo(ev.target.files?.[0] ?? null)} />
          </Campo>
        </Grade2>
      </Grade2>
      <Campo id="doc-rotulo" rotulo="Descrição" dica='(para "Outro documento")' className="mt-3">
        <input id="doc-rotulo" placeholder="opcional" value={rotulo} onChange={(ev) => setRotulo(ev.target.value)} />
      </Campo>
      <button type="button" className="botao mt-3" disabled={ocupado} onClick={anexar}>Anexar documento</button>

      {aRemover && (
        <Confirmacao titulo="Remover documento" perigo rotuloConfirmar="Remover"
          mensagem={<>Remover <strong>{aRemover.fileName}</strong> deste fornecedor? A restrição automática por certidão vencida é recalculada.</>}
          aoConfirmar={() => remover(aRemover.id)} aoFechar={() => setARemover(null)} />
      )}
    </Painel>
  );
}
