/**
 * A faixa de quatro cores do Grupo Trino — vermelho, azul, laranja e verde —, a mesma que
 * fecha o site e as artes da empresa. É o detalhe que diz de quem é o sistema; mora num
 * componente só para as quatro cores não serem copiadas em cada tela.
 */
export function FaixaDaMarca({ className = '', altura = 'h-1' }: { className?: string; altura?: string }) {
  return (
    <div aria-hidden="true" className={`flex w-full overflow-hidden rounded-full ${altura} ${className}`} data-testid="faixa-da-marca">
      <span className="flex-1 bg-marca" />
      <span className="flex-1 bg-marca-azul" />
      <span className="flex-1 bg-marca-laranja" />
      <span className="flex-1 bg-marca-verde" />
    </div>
  );
}
