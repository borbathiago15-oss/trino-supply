import { Module } from '@nestjs/common';
import { AuthModule } from '../auth/auth.module';
import { FornecedoresModule } from '../fornecedores/fornecedores.module';
import { AbrirCotacaoUseCase, EqualizarPropostasUseCase, RegistrarPropostaUseCase } from './casos-de-uso';
import { CotacoesController } from './cotacoes.controller';

@Module({
  imports: [AuthModule, FornecedoresModule],
  controllers: [CotacoesController],
  providers: [AbrirCotacaoUseCase, RegistrarPropostaUseCase, EqualizarPropostasUseCase],
  exports: [AbrirCotacaoUseCase, EqualizarPropostasUseCase],
})
export class CotacoesModule {}
