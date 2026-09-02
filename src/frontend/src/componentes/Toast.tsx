import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';

type Tipo = 'ok' | 'erro';
interface Aviso { id: number; texto: string; tipo: Tipo }
interface ContextoToast { avisar: (texto: string, tipo?: Tipo) => void }

const Contexto = createContext<ContextoToast>({ avisar: () => {} });

export function ToastProvider({ children }: { children: ReactNode }) {
  const [avisos, setAvisos] = useState<Aviso[]>([]);
  const seq = useRef(0);
  const avisar = useCallback((texto: string, tipo: Tipo = 'ok') => {
    const id = ++seq.current;
    setAvisos((a) => [...a, { id, texto, tipo }]);
    setTimeout(() => setAvisos((a) => a.filter((x) => x.id !== id)), 4200);
  }, []);
  const valor = useMemo(() => ({ avisar }), [avisar]);
  return (
    <Contexto.Provider value={valor}>
      {children}
      <div className="fixed bottom-5 right-5 z-50 flex flex-col gap-2" aria-live="polite">
        {avisos.map((a) => (
          <div key={a.id} role="status" data-testid="toast"
            className={'rounded-lg px-4 py-2.5 text-[13.5px] font-semibold shadow-lg ' +
              (a.tipo === 'erro' ? 'bg-perigo text-white' : 'bg-ok text-white')}>
            {a.texto}
          </div>
        ))}
      </div>
    </Contexto.Provider>
  );
}

export const useToast = () => useContext(Contexto);
