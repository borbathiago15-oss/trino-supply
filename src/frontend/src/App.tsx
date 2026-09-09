import { BrowserRouter, Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom';
import { ToastProvider } from '@/componentes/Toast';
import { AppLayout } from '@/layout/AppLayout';
import { Login } from '@/paginas/login/Login';
import { PedidoDetalhe } from '@/paginas/pedidos/PedidoDetalhe';
import { PedidosLista } from '@/paginas/pedidos/PedidosLista';
import { CentrosCusto } from '@/paginas/centros-custo/CentrosCusto';
import { Familias } from '@/paginas/familias/Familias';
import { FormasDePagamento } from '@/paginas/pagamentos/FormasDePagamento';
import { CondicoesDePagamento } from '@/paginas/pagamentos/CondicoesDePagamento';
import { Fornecedores } from '@/paginas/fornecedores/Fornecedores';
import { Produtos } from '@/paginas/produtos/Produtos';
import { Usuarios } from '@/paginas/usuarios/Usuarios';
import { Empresas } from '@/paginas/empresas/Empresas';
import { Comunicados } from '@/paginas/comunicados/Comunicados';
import { NovaSolicitacao } from '@/paginas/solicitacoes/NovaSolicitacao';
import { SolicitacaoEmLote } from '@/paginas/solicitacoes/SolicitacaoEmLote';
import { MeusPedidos } from '@/paginas/solicitacoes/MeusPedidos';
import { CentralDeAprovacao } from '@/paginas/aprovacoes/CentralDeAprovacao';
import { SolicitarMaterial } from '@/paginas/material/SolicitarMaterial';
import { MinhasSolicitacoes } from '@/paginas/material/MinhasSolicitacoes';
import { Contratos } from '@/paginas/contratos/Contratos';
import { Scorecard } from '@/paginas/scorecard/Scorecard';
import { Compliance } from '@/paginas/compliance/Compliance';
import { FilaDeAtendimento } from '@/paginas/estoque/FilaDeAtendimento';
import { PainelDeAtendimentos } from '@/paginas/estoque/PainelDeAtendimentos';
import { GestaoDeSolicitacoes } from '@/paginas/triagem/GestaoDeSolicitacoes';
import { TorreDeControle } from '@/paginas/torre/TorreDeControle';
import { DashboardSuprimentos } from '@/paginas/painel/DashboardSuprimentos';
import { Insights } from '@/paginas/insights/Insights';
import { Relatorios } from '@/paginas/relatorios/Relatorios';
import { AbrirCotacao } from '@/paginas/cotacoes/AbrirCotacao';
import { ProcessosDeCotacao } from '@/paginas/cotacoes/ProcessosDeCotacao';
import { ProcessoDetalhe } from '@/paginas/cotacoes/ProcessoDetalhe';
import { Portal } from '@/paginas/portal/Portal';
import { TrocarSenha } from '@/paginas/senha/TrocarSenha';
import { SessaoProvider, useSessao } from '@/sessao/SessaoProvider';
import { Carregando } from '@/componentes/basicos';
import { LimiteErro } from '@/componentes/LimiteErro';

/**
 * Só deixa passar com usuário; sem sessão, manda para o login guardando o destino.
 * Com a senha ainda provisória, a única tela que abre é a da troca (SEC-004) — o
 * servidor recusa o resto de qualquer jeito, e aqui o usuário entende o porquê.
 */
function Protegida() {
  const { usuario, carregando } = useSessao();
  const { pathname } = useLocation();
  if (carregando) return <div className="p-10"><Carregando texto="Abrindo a sessão…" /></div>;
  if (!usuario) return <Navigate to="/login" replace state={{ de: pathname }} />;
  if (usuario.mustChangePassword && pathname !== '/trocar-senha')
    return <Navigate to="/trocar-senha" replace />;
  return <Outlet />;
}

export function Rotas() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      {/* o portal é do fornecedor: sessão própria, sem o menu nem a sessão interna */}
      <Route path="/portal" element={<Portal />} />
      <Route element={<Protegida />}>
        {/* fora do AppLayout: com a senha provisória o menu não deve nem aparecer */}
        <Route path="/trocar-senha" element={<TrocarSenha />} />
        <Route element={<AppLayout />}>
          <Route index element={<Navigate to="/painel" replace />} />
          <Route path="/pedidos" element={<PedidosLista />} />
          <Route path="/pedidos/:id" element={<PedidoDetalhe />} />
          <Route path="/fornecedores" element={<Fornecedores />} />
          <Route path="/familias" element={<Familias />} />
          <Route path="/formas-pagamento" element={<FormasDePagamento />} />
          <Route path="/condicoes-pagamento" element={<CondicoesDePagamento />} />
          <Route path="/centros-custo" element={<CentrosCusto />} />
          <Route path="/produtos" element={<Produtos />} />
          <Route path="/usuarios" element={<Usuarios />} />
          <Route path="/empresas" element={<Empresas />} />
          <Route path="/comunicados" element={<Comunicados />} />
          <Route path="/solicitacoes" element={<MeusPedidos />} />
          <Route path="/solicitacoes/nova" element={<NovaSolicitacao />} />
          <Route path="/solicitacoes/lote" element={<SolicitacaoEmLote />} />
          <Route path="/aprovacoes" element={<CentralDeAprovacao />} />
          <Route path="/material" element={<MinhasSolicitacoes />} />
          <Route path="/material/nova" element={<SolicitarMaterial />} />
          <Route path="/contratos" element={<Contratos />} />
          <Route path="/scorecard" element={<Scorecard />} />
          <Route path="/compliance" element={<Compliance />} />
          <Route path="/estoque/fila" element={<FilaDeAtendimento />} />
          <Route path="/estoque/atendimentos" element={<PainelDeAtendimentos />} />
          <Route path="/gestao-solicitacoes" element={<GestaoDeSolicitacoes />} />
          <Route path="/torre" element={<TorreDeControle />} />
          <Route path="/painel" element={<DashboardSuprimentos />} />
          <Route path="/insights" element={<Insights />} />
          <Route path="/relatorios" element={<Relatorios />} />
          <Route path="/cotacoes" element={<ProcessosDeCotacao />} />
          <Route path="/cotacoes/abrir" element={<AbrirCotacao />} />
          <Route path="/cotacoes/:id" element={<ProcessoDetalhe />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/painel" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <LimiteErro>
      <BrowserRouter>
        <ToastProvider>
          <SessaoProvider>
            <Rotas />
          </SessaoProvider>
        </ToastProvider>
      </BrowserRouter>
    </LimiteErro>
  );
}
