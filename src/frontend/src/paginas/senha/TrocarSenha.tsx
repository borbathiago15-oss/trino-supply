import { useState, type FormEvent } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { trocarSenha } from '@/api/auth';
import { Aviso, Erro, Painel } from '@/componentes/basicos';
import { Campo, Nota } from '@/componentes/formulario';
import { useToast } from '@/componentes/Toast';
import { useSessao } from '@/sessao/SessaoProvider';

const MINIMO = 12;

/** Exigências que a tela consegue conferir antes de mandar — as mesmas do servidor. */
export function pendenciasDaSenha(nova: string, atual: string, nome: string, email: string): string[] {
  const faltas: string[] = [];
  const minuscula = nova.toLowerCase();
  if (nova.length < MINIMO) faltas.push(`Ter pelo menos ${MINIMO} caracteres`);

  const tipos = [/[a-z]/, /[A-Z]/, /[0-9]/, /[^a-zA-Z0-9]/].filter((r) => r.test(nova)).length;
  if (tipos < 3) faltas.push('Combinar ao menos três entre minúscula, maiúscula, número e símbolo');

  if (nova && nova === atual) faltas.push('Ser diferente da senha atual');

  const conta = email.split('@')[0];
  const partes = [conta, ...nome.split(' ')].filter((p) => p.length >= 4).map((p) => p.toLowerCase());
  if (minuscula && partes.some((p) => minuscula.includes(p))) faltas.push('Não conter o seu nome nem o seu e-mail');

  const previsiveis = ['trinosupply', 'trino supply', 'grupotrino', 'senha', 'password', '123456', 'qwerty'];
  if (minuscula && previsiveis.some((p) => minuscula.includes(p)))
    faltas.push('Não usar palavras previsíveis como "senha", "123456" ou o nome do sistema');

  return faltas;
}

/**
 * Primeiro acesso: quem cadastrou o usuário escolheu a senha, então ela vale
 * só para entrar aqui. Enquanto a troca não acontece, o servidor recusa
 * qualquer outra rota (IAM-ERR-022) — esta tela é a única saída.
 */
export function TrocarSenha() {
  const { usuario, entrou } = useSessao();
  const navegar = useNavigate();
  const { avisar } = useToast();
  const [atual, setAtual] = useState('');
  const [nova, setNova] = useState('');
  const [confirmacao, setConfirmacao] = useState('');
  const [mostrar, setMostrar] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  if (!usuario) return <Navigate to="/login" replace />;

  const provisoria = usuario.mustChangePassword === true;
  const faltas = pendenciasDaSenha(nova, atual, usuario.name, usuario.email);
  const naoConfere = confirmacao.length > 0 && confirmacao !== nova;
  const pronto = nova.length > 0 && faltas.length === 0 && confirmacao === nova && atual.length > 0;

  async function enviar(ev: FormEvent) {
    ev.preventDefault();
    setErro(null); setEnviando(true);
    try {
      entrou(await trocarSenha(atual, nova));
      avisar('Senha alterada. As outras sessões foram encerradas.');
      navegar('/painel', { replace: true });
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não consegui alterar a senha.');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="mx-auto max-w-[520px] px-4 py-8">
      <Painel titulo={provisoria ? 'Defina a sua senha' : 'Alterar senha'}>
        {provisoria ? (
          <Aviso testid="senha-provisoria">
            A senha que você usou foi definida por quem cadastrou o seu acesso. Escolha uma senha
            só sua para continuar — o sistema não abre nenhuma outra tela antes disso.
          </Aviso>
        ) : (
          <Nota>Trocar a senha encerra as suas outras sessões abertas.</Nota>
        )}

        <form onSubmit={enviar} className="mt-4" aria-label="Trocar senha">
          <Campo id="senha-atual" rotulo={provisoria ? 'Senha provisória (a que você acabou de usar)' : 'Senha atual'}>
            <input id="senha-atual" type={mostrar ? 'text' : 'password'} required autoComplete="current-password"
              value={atual} onChange={(e) => setAtual(e.target.value)} />
          </Campo>

          <Campo id="senha-nova" rotulo="Nova senha" className="mt-3">
            <input id="senha-nova" type={mostrar ? 'text' : 'password'} required autoComplete="new-password"
              value={nova} onChange={(e) => setNova(e.target.value)} />
          </Campo>

          <Campo id="senha-confirmacao" rotulo="Repita a nova senha" className="mt-3">
            <input id="senha-confirmacao" type={mostrar ? 'text' : 'password'} required autoComplete="new-password"
              value={confirmacao} onChange={(e) => setConfirmacao(e.target.value)} />
          </Campo>
          {naoConfere && <p className="mt-1 text-[12.5px] text-perigo">As duas senhas não são iguais.</p>}

          <label className="mt-3 flex items-center gap-2 font-normal">
            <input type="checkbox" className="w-auto" checked={mostrar} onChange={(e) => setMostrar(e.target.checked)} />
            Mostrar o que estou digitando
          </label>

          {nova.length > 0 && faltas.length > 0 && (
            <ul data-testid="pendencias-senha" className="mt-3 list-disc pl-5 text-[12.5px] text-texto-suave">
              {faltas.map((f) => <li key={f}>{f}</li>)}
            </ul>
          )}

          {erro && <div className="mt-3"><Erro>{erro}</Erro></div>}

          <button type="submit" className="botao mt-4 w-full" disabled={!pronto || enviando}>
            {enviando ? 'Salvando…' : 'Salvar nova senha'}
          </button>
        </form>
      </Painel>
    </div>
  );
}
