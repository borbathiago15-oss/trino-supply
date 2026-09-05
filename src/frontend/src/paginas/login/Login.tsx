import { Fragment, useState, type FormEvent, type ReactNode } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { entrar, type Usuario } from '@/api/auth';
import { useSessao } from '@/sessao/SessaoProvider';

/** Traço do ciclo de suprimentos mostrado no lado institucional. */
const ICONE = { fill: 'none', stroke: 'currentColor', strokeWidth: 1.6, strokeLinecap: 'round', strokeLinejoin: 'round' } as const;

const PASSOS: { rotulo: string; desenho: ReactNode }[] = [
  { rotulo: 'Necessidade', desenho: (<><circle cx="11" cy="11" r="6" /><path d="M20 20l-3.6-3.6" /></>) },
  { rotulo: 'Solicitação', desenho: (<><path d="M14 3H7a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V7z" /><path d="M14 3v4h4" /><path d="M9 12h6M9 16h5" /></>) },
  { rotulo: 'Compra', desenho: (<><path d="M3 5h2.2l2 9.1a1 1 0 0 0 1 .8h7.5a1 1 0 0 0 1-.8L19 8H6.4" /><circle cx="10" cy="19" r="1.4" /><circle cx="16.5" cy="19" r="1.4" /></>) },
  { rotulo: 'Recebimento', desenho: (<><path d="M3 7h10v9H3z" /><path d="M13 10h3.6L20 13v3h-7z" /><circle cx="7" cy="18" r="1.5" /><circle cx="17" cy="18" r="1.5" /></>) },
  { rotulo: 'Estoque', desenho: (<><path d="M4 10h6v6H4zM14 10h6v6h-6zM9 3h6v6H9z" /></>) },
  { rotulo: 'Resultado', desenho: (<><path d="M4 19h16" /><path d="M6 15l4.2-4.2 3 3L18 8" /><path d="M18 11V8h-3" /></>) },
];

function Passo({ rotulo, desenho, ultimo }: { rotulo: string; desenho: ReactNode; ultimo: boolean }) {
  return (
    <div role="listitem" className="flex items-center gap-[7px] text-[12.5px] text-[#d7e0ee] min-[981px]:gap-3 min-[981px]:text-[14px]">
      <span
        aria-hidden="true"
        className={`grid h-[26px] flex-[0_0_26px] place-items-center rounded-lg border bg-white/[.04]
          min-[981px]:h-[34px] min-[981px]:flex-[0_0_34px] min-[981px]:rounded-[10px]
          ${ultimo ? 'border-marca-vermelho/55 text-[#ff6b6b]' : 'border-white/[.16] text-[#b9c6da]'}`}
      >
        <svg viewBox="0 0 24 24" {...ICONE} className="h-3.5 w-3.5 min-[981px]:h-[18px] min-[981px]:w-[18px]">{desenho}</svg>
      </span>
      <span>{rotulo}</span>
    </div>
  );
}

/** Elo entre dois passos: traço horizontal na faixa compacta, vertical em duas colunas. */
function Elo() {
  return (
    <div aria-hidden="true" className="grid w-3 place-items-center min-[981px]:w-[34px]">
      <span className="block h-px w-2.5 bg-white/[.22] min-[981px]:h-3.5 min-[981px]:w-px min-[981px]:bg-gradient-to-b min-[981px]:from-white/30 min-[981px]:to-white/[.06]" />
    </div>
  );
}

/**
 * Para onde ir depois de entrar. Senha ainda provisória vence o destino
 * guardado: nada mais abre antes da troca (SEC-004).
 */
export const destinoDe = (u: Usuario, de?: string) =>
  u.mustChangePassword ? '/trocar-senha' : de ?? '/pedidos';

export function Login() {
  const { usuario, entrou } = useSessao();
  const navegar = useNavigate();
  const { state } = useLocation() as { state?: { de?: string } };
  const [email, setEmail] = useState('');
  const [senha, setSenha] = useState('');
  const [mostrar, setMostrar] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  if (usuario) return <Navigate to={destinoDe(usuario, state?.de)} replace />;

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setErro(null); setEnviando(true);
    try {
      const logado = await entrar(email, senha);
      entrou(logado);
      navegar(destinoDe(logado, state?.de), { replace: true });
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Falha ao entrar.');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <main className="grid min-h-screen bg-marca-navy min-[981px]:grid-cols-[1.1fr_.9fr]">
      {/* lado institucional: marca, propósito e ciclo de suprimentos */}
      <section
        className="relative flex flex-col justify-center gap-5 overflow-hidden
          bg-gradient-to-b from-marca-navy2 to-[#04142f] px-[18px] pb-5 pt-[22px] text-[#e8edf6]
          min-[561px]:gap-5 min-[561px]:bg-none min-[561px]:px-6 min-[561px]:pb-6 min-[561px]:pt-7
          min-[981px]:gap-11 min-[981px]:px-[clamp(28px,4.5vw,72px)] min-[981px]:py-14"
      >
        {/* a partir do tablet o fundo vira o radial da marca; no celular fica o degradê simples acima */}
        <div
          aria-hidden="true" className="pointer-events-none absolute inset-0 hidden min-[561px]:block"
          style={{ background: 'radial-gradient(120% 95% at 12% 0%, #0a2450 0%, #04142f 55%, #020c1e 100%)' }}
        />
        {/* malha discreta de conexões — só CSS, sem imagem de fundo */}
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 hidden min-[561px]:block"
          style={{
            backgroundImage:
              'linear-gradient(rgba(255,255,255,.05) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,.05) 1px, transparent 1px)',
            backgroundSize: '58px 58px',
            WebkitMaskImage: 'radial-gradient(85% 70% at 28% 18%, #000 0%, transparent 78%)',
            maskImage: 'radial-gradient(85% 70% at 28% 18%, #000 0%, transparent 78%)',
          }}
        />

        <div className="relative w-full max-w-[520px] motion-safe:animate-entrada">
          <img
            src="/assets/brand/trino-supply-logo.png" width={640} height={204}
            alt="Trino Supply — Gestão Inteligente de Suprimentos"
            className="block h-auto w-[min(240px,82%)] min-[561px]:w-[min(280px,70%)] min-[981px]:w-[min(360px,78%)]"
          />
          <p className="mt-3.5 text-[11px] font-bold uppercase tracking-[2px] text-marca-prata min-[561px]:mt-5 min-[561px]:text-[12px] min-[561px]:tracking-[2.4px]">
            Enterprise Supply Management
          </p>
          <p className="mt-3 hidden max-w-[46ch] text-[13.5px] leading-relaxed text-[#cfd9e8] min-[561px]:block min-[561px]:text-[14.5px] min-[981px]:text-[15.5px]">
            Uma plataforma integrada para gestão de suprimentos, compras, fornecedores, contratos e estoque.
          </p>
        </div>

        <div
          role="list" aria-label="Ciclo de suprimentos atendido pela plataforma"
          className="relative flex w-full max-w-[520px] flex-row flex-wrap items-center gap-x-1.5 gap-y-2
            motion-safe:animate-entrada min-[981px]:flex-col min-[981px]:flex-nowrap min-[981px]:items-stretch min-[981px]:gap-0.5"
        >
          <div aria-hidden="true" className="mb-0.5 w-full text-[11px] font-bold uppercase tracking-[1.6px] text-[#7c8ba5] min-[981px]:mb-2.5">
            Do pedido ao resultado
          </div>
          {PASSOS.map((p, i) => (
            <Fragment key={p.rotulo}>
              {i > 0 && <Elo />}
              <Passo rotulo={p.rotulo} desenho={p.desenho} ultimo={i === PASSOS.length - 1} />
            </Fragment>
          ))}
        </div>

        <div className="relative hidden w-full max-w-[520px] border-t border-white/[.12] pt-4 min-[561px]:block min-[981px]:mt-auto motion-safe:animate-entrada">
          <span className="text-[13px] font-extrabold tracking-[3px] text-white">GRUPO TRINO</span>
          <span aria-hidden="true" className="mt-1.5 block h-0.5 w-[42px] bg-marca-vermelho" />
          <span className="mt-2.5 block max-w-[44ch] text-[12px] text-[#93a2ba] min-[561px]:text-[13px]">
            O Trino Supply é a plataforma corporativa de suprimentos do Grupo Trino.
          </span>
        </div>
      </section>

      {/* painel de acesso */}
      <section className="flex items-center justify-center bg-superficie px-5 pb-10 pt-7 min-[981px]:px-[clamp(20px,4vw,56px)] min-[981px]:py-10">
        <div className="w-full max-w-[460px] min-[981px]:max-w-[400px] motion-safe:animate-entrada motion-safe:[animation-delay:.06s]">
          <span aria-hidden="true" className="mb-4 block h-[3px] w-[34px] rounded-sm bg-marca-vermelho" />
          <h1 className="mb-1.5 text-[22px] font-bold -tracking-[.2px]">Acesse sua conta</h1>
          <p className="mb-1 text-[13.5px] text-texto-suave">Entre com o e-mail corporativo cadastrado pelo administrador.</p>

          <form onSubmit={enviar} autoComplete="on" aria-label="Entrar">
            <label htmlFor="email" className="mb-1.5 mt-4">E-mail</label>
            <input
              id="email" name="email" type="email" required autoComplete="username" inputMode="email"
              placeholder="nome@empresa.com.br" className="px-3.5 py-[13px] text-[15px]"
              value={email} onChange={(e) => setEmail(e.target.value)}
            />

            <label htmlFor="password" className="mb-1.5 mt-4">Senha</label>
            <div className="relative">
              <input
                id="password" name="password" type={mostrar ? 'text' : 'password'} required autoComplete="current-password"
                className="py-[13px] pl-3.5 pr-[84px] text-[15px]"
                value={senha} onChange={(e) => setSenha(e.target.value)}
              />
              <button
                type="button" aria-controls="password" aria-pressed={mostrar}
                aria-label={mostrar ? 'Ocultar senha' : 'Mostrar senha'}
                onClick={() => setMostrar(!mostrar)}
                className="absolute right-1.5 top-1/2 -translate-y-1/2 rounded-md border-0 bg-transparent px-2.5 py-1.5
                  text-[12.5px] font-bold text-marca hover:bg-superficie-suave"
              >
                {mostrar ? 'Ocultar' : 'Mostrar'}
              </button>
            </div>

            <button type="submit" className="botao mt-[22px] w-full py-3.5 text-[15px]" disabled={enviando}>
              {enviando ? 'Entrando…' : 'Entrar'}
            </button>
          </form>

          {erro && (
            <p role="alert" aria-live="polite" className="mt-3.5 rounded-lg border border-perigo/25 bg-perigo-fundo px-3 py-[11px] text-[13.5px] text-perigo">
              {erro}
            </p>
          )}

          <p className="mt-4 text-[12.5px] leading-normal text-texto-suave">
            Esqueceu a senha? Solicite a redefinição ao administrador do sistema.
          </p>
          <p className="mt-4 text-[12.5px] text-texto-suave min-[561px]:hidden">
            Plataforma corporativa de suprimentos do <strong>GRUPO TRINO</strong>.
          </p>
          <div className="mt-[22px] flex items-center justify-center gap-[7px] text-center text-[12px] text-texto-suave">
            <i aria-hidden="true" className="h-[7px] w-[7px] flex-none rounded-full bg-ok shadow-[0_0_0_3px_rgba(21,128,61,.14)]" />
            Ambiente corporativo · acesso restrito e auditado
          </div>
        </div>
      </section>
    </main>
  );
}
