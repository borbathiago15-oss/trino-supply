import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Dialogo } from '@/componentes/Dialogo';
import { Campo } from '@/componentes/formulario';
import {
  abrirChamado, anexarAoChamado, infoDoNavegador, ROTULO_CATEGORIA,
  type CategoriaDoChamado, type Chamado,
} from '@/api/suporte';
import type { TelaAtual } from '@/manual/telas';

/** As mesmas réguas do servidor (`ChamadoService`): a tela avisa antes, em vez de o erro avisar depois. */
export const ASSUNTO_MINIMO = 5;
export const DESCRICAO_MINIMA = 10;

const ACEITOS = 'image/png,image/jpeg,application/pdf';

/**
 * Abrir um chamado de dentro da tela onde a dúvida apareceu.
 *
 * A tela vai junto sem a pessoa digitar: "não consigo aprovar" quer dizer coisas diferentes
 * na Central de Aprovação e no processo, e é o dado que quem abre nunca lembra de escrever.
 *
 * Depois de abrir, o diálogo fica com o número do chamado em vez de sumir: quem liga para o
 * suporte vai precisar dele, e um aviso que some em quatro segundos não serve para isso.
 */
export function DialogoDeSuporte({ tela, rota, aoFechar }:
  { tela: TelaAtual; rota: string; aoFechar: () => void }) {
  const [categoria, setCategoria] = useState<CategoriaDoChamado>('DUVIDA');
  const [assunto, setAssunto] = useState('');
  const [descricao, setDescricao] = useState('');
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [aberto, setAberto] = useState<{ chamado: Chamado; falhaDoAnexo: string | null } | null>(null);

  const faltaAssunto = assunto.trim().length < ASSUNTO_MINIMO;
  const faltaDescricao = descricao.trim().length < DESCRICAO_MINIMA;

  async function enviar() {
    if (faltaAssunto || faltaDescricao) return;
    setEnviando(true);
    setErro(null);
    try {
      const { ticket, firstMessageId } = await abrirChamado({
        category: categoria, subject: assunto.trim(), description: descricao.trim(),
        screen: rota, screenLabel: tela.rotulo, clientInfo: infoDoNavegador(),
      });
      // o chamado já existe: anexo recusado não desfaz a abertura, só é dito
      let falhaDoAnexo: string | null = null;
      if (arquivo && firstMessageId) {
        try { await anexarAoChamado(ticket.id, firstMessageId, arquivo); }
        catch (e) { falhaDoAnexo = e instanceof Error ? e.message : 'O anexo não foi enviado.'; }
      }
      setAberto({ chamado: ticket, falhaDoAnexo });
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não consegui abrir o chamado.');
    } finally {
      setEnviando(false);
    }
  }

  if (aberto) {
    return (
      <Dialogo titulo="Chamado aberto" aoFechar={aoFechar} acoes={
        <>
          <Link to={`/suporte/${aberto.chamado.id}`} className="botao-secundario" onClick={aoFechar}>Ver o chamado</Link>
          <button type="button" className="botao" onClick={aoFechar}>Voltar ao que eu fazia</button>
        </>
      }>
        <p>
          Seu chamado é o <strong data-testid="numero-do-chamado">{aberto.chamado.number}</strong>, sobre a tela{' '}
          <strong>{aberto.chamado.screenLabel}</strong>.
        </p>
        <p className="mt-2 text-texto-suave">Quando o suporte responder, o aviso chega no sino.</p>
        {aberto.falhaDoAnexo && (
          <p role="alert" className="mt-3 rounded-lg bg-aviso-fundo px-3 py-2 text-aviso">
            O chamado foi aberto, mas o anexo não: {aberto.falhaDoAnexo} Envie de novo pela conversa do chamado.
          </p>
        )}
      </Dialogo>
    );
  }

  return (
    <Dialogo titulo="Abrir chamado de suporte" aoFechar={aoFechar} largura="max-w-[560px]" acoes={
      <>
        <button type="button" className="botao-secundario" onClick={aoFechar}>Cancelar</button>
        <button type="button" className="botao" onClick={enviar} disabled={enviando || faltaAssunto || faltaDescricao}>
          {enviando ? 'Enviando…' : 'Abrir chamado'}
        </button>
      </>
    }>
      <div className="space-y-3">
        <p className="rounded-lg bg-slate-50 px-3 py-2 text-[13px]">
          Tela: <strong data-testid="tela-do-chamado">{tela.rotulo}</strong>
          <span className="block text-[12px] text-texto-suave">vai junto com o chamado — não precisa escrever onde você estava</span>
        </p>

        <Campo id="ch-categoria" rotulo="Do que se trata">
          <select id="ch-categoria" value={categoria} onChange={(e) => setCategoria(e.target.value as CategoriaDoChamado)}>
            {(Object.keys(ROTULO_CATEGORIA) as CategoriaDoChamado[]).map((c) => (
              <option key={c} value={c}>{ROTULO_CATEGORIA[c]}</option>
            ))}
          </select>
        </Campo>

        <Campo id="ch-assunto" rotulo="Assunto" dica={`(mínimo de ${ASSUNTO_MINIMO} caracteres)`}>
          <input id="ch-assunto" maxLength={150} value={assunto} onChange={(e) => setAssunto(e.target.value)}
            placeholder="Ex.: o botão de aprovar não aparece" />
        </Campo>

        <Campo id="ch-descricao" rotulo="O que aconteceu" dica={`(mínimo de ${DESCRICAO_MINIMA} caracteres)`}>
          <textarea id="ch-descricao" rows={5} maxLength={4000} value={descricao} onChange={(e) => setDescricao(e.target.value)}
            placeholder="O que você tentou fazer, o que esperava e o que apareceu. Se houve mensagem de erro, copie o código (ex.: RFQ-ERR-030)." />
        </Campo>

        <Campo id="ch-anexo" rotulo="Print ou arquivo" dica="(opcional — imagem ou PDF, até 10 MB)">
          <input id="ch-anexo" type="file" accept={ACEITOS} onChange={(e) => setArquivo(e.target.files?.[0] ?? null)} />
        </Campo>

        {erro && <p role="alert" className="rounded-lg bg-perigo-fundo px-3 py-2 text-perigo">{erro}</p>}
      </div>
    </Dialogo>
  );
}
