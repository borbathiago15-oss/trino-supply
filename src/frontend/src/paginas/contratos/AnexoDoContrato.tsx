import { useState, type ChangeEvent } from 'react';
import {
  anexarDocumento, DOCUMENTOS_DO_CONTRATO, ROTULO_DOCUMENTO, type TipoDocumento,
} from '@/api/fornecedores';
import { Campo, Grade2 } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';

/** Os tipos que se anexam pelo contrato: os papéis dele, mais "outro" para o que não tem nome. */
export const TIPOS_ANEXAVEIS: TipoDocumento[] = [...DOCUMENTOS_DO_CONTRATO, 'OUTRO'];

/**
 * Anexar o contrato assinado, um aditivo ou outro papel do contrato.
 *
 * <p>
 * Num lugar só porque <strong>duas telas anexam o mesmo documento</strong>: a ficha do contrato
 * (depois de ele existir) e o próprio formulário do contrato (na hora de fechá-lo, que é quando
 * o comprador tem o PDF assinado na mão). Duplicar faria uma delas gravar com validade e a outra
 * sem, e a lista de documentos passaria a depender de por onde se entrou.
 * </p>
 *
 * <p>
 * O documento é do <strong>fornecedor</strong>, não do contrato — é lá que o modelo o guarda —,
 * então ele pode ser anexado antes mesmo de o contrato ser salvo: o que o anexo precisa é do
 * fornecedor escolhido, e no formulário ele já está.
 * </p>
 *
 * <p>
 * O papel do contrato <strong>não leva validade</strong>: a dele é a vigência, que já está no
 * contrato. É por isso que ele não acende o aviso de certidão vencida nem restringe a
 * homologação, ao contrário das certidões.
 * </p>
 */
export function FormularioDeAnexo({ fornecedorId, aoAnexado, aoCancelar, idPrefixo = 'anexo' }: {
  fornecedorId: string;
  aoAnexado: () => void;
  aoCancelar: () => void;
  /** Para as duas telas não repetirem o mesmo `id` quando convivem na mesma página. */
  idPrefixo?: string;
}) {
  const { avisar } = useToast();
  const [tipo, setTipo] = useState<TipoDocumento>('CONTRATO');
  const [rotulo, setRotulo] = useState('');
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [ocupado, setOcupado] = useState(false);

  async function anexar() {
    if (!arquivo) { avisar('Escolha o arquivo.', 'erro'); return; }
    setOcupado(true);
    try {
      // sem validade: a do papel do contrato é a vigência, que já está no contrato
      await anexarDocumento(fornecedorId, arquivo, tipo, '', rotulo);
      avisar('Documento anexado ao contrato.');
      setArquivo(null); setRotulo('');
      aoAnexado();
    } catch (e) {
      avisar(e instanceof Error ? e.message : 'Falha ao anexar o documento.', 'erro');
    } finally { setOcupado(false); }
  }

  return (
    <div className="mt-4 rounded-lg border border-borda p-3" data-testid="anexar-documento-contrato">
      <Grade2>
        <Campo id={`${idPrefixo}-doc-tipo`} rotulo="Documento">
          <select id={`${idPrefixo}-doc-tipo`} value={tipo}
            onChange={(ev) => setTipo(ev.target.value as TipoDocumento)}>
            {TIPOS_ANEXAVEIS.map((t) => <option key={t} value={t}>{ROTULO_DOCUMENTO[t]}</option>)}
          </select>
        </Campo>
        <Campo id={`${idPrefixo}-doc-arquivo`} rotulo="Arquivo">
          <input id={`${idPrefixo}-doc-arquivo`} type="file" accept=".pdf,.png,.jpg,.jpeg,.docx"
            onChange={(ev: ChangeEvent<HTMLInputElement>) => setArquivo(ev.target.files?.[0] ?? null)} />
        </Campo>
      </Grade2>
      <Campo id={`${idPrefixo}-doc-rotulo`} rotulo="Descrição"
        dica="(opcional — ex.: 1º aditivo, renovação 2027)" className="mt-3">
        <input id={`${idPrefixo}-doc-rotulo`} value={rotulo} onChange={(ev) => setRotulo(ev.target.value)} />
      </Campo>
      <div className="mt-3 flex gap-2">
        <button type="button" className="botao" disabled={ocupado} onClick={anexar}>
          {ocupado ? 'Anexando…' : 'Anexar'}
        </button>
        <button type="button" className="botao-secundario" onClick={aoCancelar}>Cancelar</button>
      </div>
    </div>
  );
}
