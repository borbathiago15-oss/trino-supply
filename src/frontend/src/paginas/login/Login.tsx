import { useState, type FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { entrar } from '@/api/auth';
import { useSessao } from '@/sessao/SessaoProvider';

export function Login() {
  const { usuario, entrou } = useSessao();
  const navegar = useNavigate();
  const { state } = useLocation() as { state?: { de?: string } };
  const [email, setEmail] = useState('');
  const [senha, setSenha] = useState('');
  const [mostrar, setMostrar] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  if (usuario) return <Navigate to={state?.de ?? '/pedidos'} replace />;

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setErro(null); setEnviando(true);
    try {
      entrou(await entrar(email, senha));
      navegar(state?.de ?? '/pedidos', { replace: true });
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Falha ao entrar.');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-fundo px-4">
      <form onSubmit={enviar} className="w-full max-w-[420px] rounded-painel bg-white px-8 pb-8 pt-7 shadow-xl" aria-label="Entrar">
        <img src="/assets/brand/trino-supply-logo.png" width={640} height={204} alt="Trino Supply" className="mx-auto mb-6 block h-auto w-[78%] max-w-[300px]" />
        <div className="mb-3">
          <label htmlFor="email">E-mail</label>
          <input id="email" type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div className="mb-4">
          <label htmlFor="password">Senha</label>
          <div className="flex gap-2">
            <input id="password" type={mostrar ? 'text' : 'password'} autoComplete="current-password" required
              value={senha} onChange={(e) => setSenha(e.target.value)} />
            <button type="button" className="botao-secundario shrink-0" aria-pressed={mostrar}
              aria-label={mostrar ? 'Ocultar senha' : 'Mostrar senha'} onClick={() => setMostrar(!mostrar)}>
              {mostrar ? 'Ocultar' : 'Mostrar'}
            </button>
          </div>
        </div>
        {erro && <p role="alert" className="mb-3 rounded-lg bg-perigo-fundo px-3 py-2 text-[13px] text-perigo">{erro}</p>}
        <button type="submit" className="botao w-full" disabled={enviando}>{enviando ? 'Entrando…' : 'Entrar'}</button>
        <p className="sub mt-4 text-center">Prefere a versão anterior? <a className="text-marca underline" href="/">Abrir o Trino Supply clássico</a></p>
      </form>
    </div>
  );
}
