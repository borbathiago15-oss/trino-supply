import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Badge, Carregando, Erro, Painel } from '@/componentes/basicos';
import { Campo } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { abrirBlob } from '@/api/cliente';
import { baixarDocumento } from '@/api/documentos';
import {
  anexarAoChamado, lerChamado, responderChamado, ROTULO_CATEGORIA, type ChamadoCompleto,
} from '@/api/suporte';
import { dataHora } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';
import { DESCRICAO_MINIMA } from './DialogoDeSuporte';
import { rotuloDaSituacao, TOM_DA_SITUACAO } from './situacao';

const ACEITOS = 'image/png,image/jpeg,application/pdf';

/** Uma frase que diz de quem é a vez — a mesma pergunta que a Torre responde para a compra. */
function aVezDe(c: ChamadoCompleto): string {
  const t = c.ticket;
  if (t.status === 'RESOLVIDO')
    return `Resolvido${t.resolvedByLabel ? ` por ${t.resolvedByLabel}` : ''} em ${dataHora(t.resolvedAt)}. Se não resolveu, responda aqui: o chamado reabre.`;
  if (t.status === 'AGUARDANDO_USUARIO')
    return c.souDoSuporte ? 'Aguardando a resposta de quem abriu.' : 'O suporte respondeu — a vez é sua.';
  return c.souDoSuporte
    ? (t.assignedToLabel ? `Com ${t.assignedToLabel}.` : 'Ninguém assumiu ainda: responder assume o chamado.')
    : (t.assignedToLabel ? `Com ${t.assignedToLabel}, do suporte.` : 'Na fila do suporte.');
}

/**
 * A conversa de um chamado, na ordem em que aconteceu. A descrição da abertura é a primeira
 * mensagem, e a tela de onde ele foi aberto é um link: quem atende vai direto ao lugar.
 */
export function Chamado() {
  const { id = '' } = useParams();
  const { avisar } = useToast();
  const leitura = useCarregar((s) => lerChamado(id, s), [id]);
  const [texto, setTexto] = useState('');
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [chaveDoArquivo, setChaveDoArquivo] = useState(0);

  if (leitura.erro && !leitura.dados) return <Erro>{leitura.erro}</Erro>;
  if (!leitura.dados) return <Carregando />;
  const c = leitura.dados;
  const t = c.ticket;
  const resolvido = t.status === 'RESOLVIDO';
  const limpo = texto.trim();
  // o suporte não resolve em silêncio (CH-ERR-021) — o botão já diz isso antes do servidor
  const podeResolverComoSuporte = limpo.length >= DESCRICAO_MINIMA;

  async function enviar(resolver: boolean) {
    setEnviando(true);
    try {
      const r = await responderChamado(id, { text: limpo || undefined, resolve: resolver });
      if (arquivo && r.message) {
        try { await anexarAoChamado(id, r.message.id, arquivo); }
        catch (e) { avisar(`Mensagem enviada, mas o anexo não: ${e instanceof Error ? e.message : ''}`, 'erro'); }
      }
      setTexto('');
      setArquivo(null);
      setChaveDoArquivo((k) => k + 1);
      avisar(resolver ? 'Chamado encerrado.' : 'Mensagem enviada.');
      leitura.recarregar();
    } catch (e) {
      avisar(e instanceof Error ? e.message : 'Não consegui enviar.', 'erro');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="space-y-4">
      <p><Link to="/suporte" className="text-[13px]">← Chamados de suporte</Link></p>

      <Painel titulo={`${t.number} — ${t.subject}`} acoes={
        <Badge classe={TOM_DA_SITUACAO[t.status]}>{rotuloDaSituacao(t, c.souDoSuporte)}</Badge>
      }>
        <p className="mb-2 text-[14px] font-semibold text-slate-800" data-testid="a-vez-de">{aVezDe(c)}</p>
        <dl className="grid grid-cols-1 gap-x-6 gap-y-1 text-[13px] sm:grid-cols-2">
          <div><dt className="inline text-texto-suave">Tela: </dt>
            <dd className="inline">{t.screen ? <Link to={t.screen}>{t.screenLabel}</Link> : t.screenLabel}</dd></div>
          <div><dt className="inline text-texto-suave">Tipo: </dt><dd className="inline">{ROTULO_CATEGORIA[t.category]}</dd></div>
          <div><dt className="inline text-texto-suave">Aberto por: </dt><dd className="inline">{t.createdByLabel}, em {dataHora(t.createdAt)}</dd></div>
          {c.souDoSuporte && c.clientInfo && (
            <div className="sm:col-span-2"><dt className="inline text-texto-suave">Navegador: </dt>
              <dd className="inline break-all text-[12px]">{c.clientInfo}</dd></div>
          )}
        </dl>
      </Painel>

      <Painel titulo="Conversa">
        <ol className="space-y-3" data-testid="conversa">
          {c.messages.map((m) => (
            <li key={m.id}
              className={'rounded-lg border px-4 py-3 ' + (m.fromSupport ? 'border-blue-100 bg-blue-50/60 sm:ml-10' : 'border-slate-200 bg-white sm:mr-10')}>
              <p className="mb-1 text-[12px] text-texto-suave">
                <strong className="text-slate-700">{m.authorLabel}</strong>
                {m.fromSupport && ' · suporte'} · {dataHora(m.createdAt)}
              </p>
              <p className="whitespace-pre-wrap text-[13.5px]">{m.text}</p>
              {m.attachmentId && (
                <button type="button" className="botao-secundario mt-2 !py-1 text-[12.5px]"
                  onClick={async () => {
                    try { abrirBlob(await baixarDocumento(m.attachmentId!)); }
                    catch (e) { avisar(e instanceof Error ? e.message : 'Falha ao abrir o anexo.', 'erro'); }
                  }}>
                  📎 {m.attachmentName ?? 'anexo'}
                </button>
              )}
            </li>
          ))}
        </ol>

        <div className="mt-4 space-y-3 border-t border-slate-100 pt-4">
          <Campo id="ch-resposta" rotulo={resolvido ? 'Responder (reabre o chamado)' : 'Responder'}>
            <textarea id="ch-resposta" rows={4} maxLength={4000} value={texto} onChange={(e) => setTexto(e.target.value)} />
          </Campo>
          <Campo id="ch-resposta-anexo" rotulo="Print ou arquivo" dica="(opcional — imagem ou PDF, até 10 MB)">
            <input key={chaveDoArquivo} id="ch-resposta-anexo" type="file" accept={ACEITOS}
              onChange={(e) => setArquivo(e.target.files?.[0] ?? null)} />
          </Campo>
          <div className="flex flex-wrap justify-end gap-2">
            {!resolvido && !c.souDoSuporte && (
              <button type="button" className="botao-secundario" disabled={enviando} onClick={() => enviar(true)}>
                Encerrar chamado
              </button>
            )}
            {!resolvido && c.souDoSuporte && (
              <button type="button" className="botao-secundario" disabled={enviando || !podeResolverComoSuporte}
                title={podeResolverComoSuporte ? undefined : 'Diga o que foi feito antes de resolver'}
                onClick={() => enviar(true)}>
                Responder e resolver
              </button>
            )}
            <button type="button" className="botao" disabled={enviando || !limpo} onClick={() => enviar(false)}>
              {enviando ? 'Enviando…' : 'Enviar resposta'}
            </button>
          </div>
        </div>
      </Painel>
    </div>
  );
}
