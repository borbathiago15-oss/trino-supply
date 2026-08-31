import { Module } from '@nestjs/common';
import { AuthModule } from '../auth/auth.module';
import { FornecedoresModule } from '../fornecedores/fornecedores.module';
import { RequisicoesModule } from '../requisicoes/requisicoes.module';
import { EmitirPedidoCompraUseCase, RegistrarRecebimentoUseCase } from './casos-de-uso';
import { PedidosController } from './pedidos.controller';

@Module({
  imports: [AuthModule, FornecedoresModule, RequisicoesModule],
  controllers: [PedidosController],
  providers: [EmitirPedidoCompraUseCase, RegistrarRecebimentoUseCase],
  exports: [EmitirPedidoCompraUseCase, RegistrarRecebimentoUseCase],
})
export class PedidosModule {}
