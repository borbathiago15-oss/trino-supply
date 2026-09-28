import { useTema } from '@/tema/tema';

/**
 * Claro ou escuro, ao alcance de um toque, em todo cabeçalho. O rótulo diz o que o toque faz
 * (não o estado atual): "Tema escuro" é o que se ganha ao apertar quando está claro.
 */
export function BotaoDeTema({ className = '' }: { className?: string }) {
  const [tema, alternar] = useTema();
  const escuro = tema === 'escuro';
  return (
    <button type="button" onClick={alternar} aria-pressed={escuro} data-testid="botao-de-tema"
      aria-label={escuro ? 'Tema claro' : 'Tema escuro'} title={escuro ? 'Tema claro' : 'Tema escuro'}
      className={`botao-secundario !px-2.5 !py-1.5 text-[15px] leading-none ${className}`}>
      <span aria-hidden="true">{escuro ? '☀' : '☾'}</span>
    </button>
  );
}
