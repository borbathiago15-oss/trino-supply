import QRCode from 'qrcode';
import { useState, type FormEvent } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { ativarMfa, desativarMfa, estadoDoMfa, iniciarMfa } from '@/api/auth';
import { Aviso, Carregando, Erro, Painel } from '@/componentes/basicos';
import { Campo, Nota } from '@/componentes/formulario';
import { paginaInicial } from '@/dominio/papeis';
import { useSessao } from '@/sessao/SessaoProvider';
import { dataHora } from '@/util/formato';
import { useCarregar } from '@/util/useCarregar';

const mensagem = (e: unknown, padrao: string) => (e instanceof Error ? e.message : padrao);

/**
 * A verificação em duas etapas da própria pessoa: cadastrar o autenticador (QR ou segredo
 * digitado), confirmar com o primeiro código, guardar os códigos de recuperação e, para quem
 * o papel não obriga, desligar. Para diretor e administrador ela é obrigatória: quem entra sem
 * ela cai aqui e o servidor não abre outra tela (IAM-ERR-024) — o mesmo desenho da senha
 * provisória. Fica fora da casca, como a troca de senha, pelo mesmo motivo.
 */
export function Autenticador() {
  const { usuario, entrou } = useSessao();
  const navegar = useNavigate();
  const { dados: estado, erro, carregando, recarregar } = useCarregar(() => estadoDoMfa());
  const [cadastro, setCadastro] = useState<{ secret: string; otpauthUrl: string; qr: string } | null>(null);
  const [codigo, setCodigo] = useState('');
  const [codigosDeRecuperacao, setCodigosDeRecuperacao] = useState<string[] | null>(null);
  const [erroAcao, setErroAcao] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  if (!usuario) return <Navigate to="/login" replace />;
  const obrigatorio = usuario.mustSetupMfa === true;

  async function comecar() {
    setErroAcao(null); setEnviando(true);
    try {
      const c = await iniciarMfa();
      // o QR é o mesmo texto do segredo, só que para a câmera: nada sai da tela
      const qr = await QRCode.toDataURL(c.otpauthUrl, { width: 220, margin: 1 });
      setCadastro({ ...c, qr });
      setCodigo('');
    } catch (e) {
      setErroAcao(mensagem(e, 'Não consegui começar o cadastro.'));
    } finally {
      setEnviando(false);
    }
  }

  async function confirmar(ev: FormEvent) {
    ev.preventDefault();
    setErroAcao(null); setEnviando(true);
    try {
      const { usuario: atualizado, recoveryCodes } = await ativarMfa(codigo);
      entrou(atualizado);
      setCadastro(null);
      setCodigosDeRecuperacao(recoveryCodes);
      recarregar();
    } catch (e) {
      setErroAcao(mensagem(e, 'Não consegui confirmar o código.'));
    } finally {
      setEnviando(false);
    }
  }

  async function desligar(ev: FormEvent) {
    ev.preventDefault();
    setErroAcao(null); setEnviando(true);
    try {
      await desativarMfa(codigo);
      entrou({ ...usuario!, mfaEnabled: false });
      setCodigo('');
      recarregar();
    } catch (e) {
      setErroAcao(mensagem(e, 'Não consegui desligar.'));
    } finally {
      setEnviando(false);
    }
  }

  const ativa = estado?.enabled === true;

  return (
    <div className="mx-auto max-w-[560px] px-4 py-8">
      <Painel titulo="Verificação em duas etapas">
        {obrigatorio && !ativa && (
          <Aviso testid="mfa-obrigatorio">
            O seu papel exige a verificação em duas etapas. Cadastre um aplicativo autenticador para
            continuar — o sistema não abre nenhuma outra tela antes disso.
          </Aviso>
        )}
        {carregando && <Carregando texto="Consultando…" />}
        {erro && <Erro>{erro}</Erro>}

        {codigosDeRecuperacao && (
          <div data-testid="codigos-recuperacao" className="mt-3">
            <p className="font-semibold">Guarde estes códigos de recuperação.</p>
            <p className="text-[13px] text-texto-suave">
              Cada um entra uma vez, no lugar do código do aplicativo, se você perder o telefone. Eles não
              aparecem de novo.
            </p>
            <ul className="mt-2 grid grid-cols-2 gap-1 font-mono text-[14px]">
              {codigosDeRecuperacao.map((c) => <li key={c} className="rounded border border-borda px-2 py-1">{c}</li>)}
            </ul>
            <button type="button" className="botao mt-4 w-full" onClick={() => navegar(paginaInicial(usuario), { replace: true })}>
              Já guardei os códigos
            </button>
          </div>
        )}

        {!codigosDeRecuperacao && estado && ativa && (
          <div className="mt-3" data-testid="mfa-ativa">
            <p>Ligada desde <strong>{dataHora(estado.enabledAt)}</strong>. Códigos de recuperação restantes: <strong>{estado.recoveryCodesLeft}</strong>.</p>
            {estado.required ? (
              <Nota>O seu papel exige a segunda etapa, por isso ela não pode ser desligada aqui. Para trocar de telefone, peça ao administrador para redefinir o autenticador.</Nota>
            ) : (
              <form onSubmit={desligar} className="mt-4" aria-label="Desligar a verificação em duas etapas">
                <Campo id="mfa-codigo" rotulo="Para desligar, digite o código atual do aplicativo (ou um código de recuperação)">
                  <input id="mfa-codigo" inputMode="numeric" autoComplete="one-time-code" value={codigo} onChange={(e) => setCodigo(e.target.value)} />
                </Campo>
                <button type="submit" className="botao-perigo mt-3" disabled={enviando || codigo.trim().length < 6}>Desligar</button>
              </form>
            )}
          </div>
        )}

        {!codigosDeRecuperacao && estado && !ativa && !cadastro && (
          <div className="mt-3">
            <ol className="list-decimal pl-5 text-[13.5px] text-texto-suave">
              <li>Instale um aplicativo autenticador no telefone (Google Authenticator, Microsoft Authenticator ou Authy).</li>
              <li>Clique em começar, leia o QR com o aplicativo (ou digite o segredo).</li>
              <li>Digite o primeiro código que ele mostrar.</li>
            </ol>
            <button type="button" className="botao mt-4" data-testid="comecar-mfa" onClick={comecar} disabled={enviando}>
              {enviando ? 'Preparando…' : 'Começar o cadastro'}
            </button>
          </div>
        )}

        {cadastro && (
          <form onSubmit={confirmar} className="mt-3" aria-label="Confirmar o autenticador">
            <div className="flex flex-col items-center gap-2 sm:flex-row sm:items-start">
              <img src={cadastro.qr} alt="QR code do autenticador" width={220} height={220} className="rounded border border-borda bg-white" />
              <div className="min-w-0 text-[13px] text-texto-suave">
                <p>Não consegue ler o QR? Digite este segredo no aplicativo:</p>
                <code data-testid="segredo-mfa" className="mt-1 block break-all rounded bg-superficie-suave px-2 py-1 font-mono text-[13px] text-texto">{cadastro.secret}</code>
              </div>
            </div>
            <Campo id="mfa-codigo" rotulo="Código que o aplicativo mostra agora" className="mt-4">
              <input id="mfa-codigo" inputMode="numeric" autoComplete="one-time-code" placeholder="000000" value={codigo} onChange={(e) => setCodigo(e.target.value)} />
            </Campo>
            <button type="submit" className="botao mt-3" disabled={enviando || codigo.trim().length < 6}>
              {enviando ? 'Confirmando…' : 'Confirmar código'}
            </button>
          </form>
        )}

        {erroAcao && <div className="mt-3"><Erro>{erroAcao}</Erro></div>}

        {!obrigatorio && (
          <p className="mt-6 text-[12.5px] text-texto-suave">
            <Link to="/trocar-senha" className="text-marca underline">Alterar senha</Link>
            {' · '}
            <Link to={paginaInicial(usuario)} className="text-marca underline">Voltar ao sistema</Link>
          </p>
        )}
      </Painel>
    </div>
  );
}
