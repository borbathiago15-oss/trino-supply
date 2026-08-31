import { Module } from '@nestjs/common';
import { AuthModule } from '../auth/auth.module';
import {
  AssumirTriagemUseCase,
  DevolverAjusteUseCase,
  ReenviarRequisicaoUseCase,
  SubmeterRequisicaoUseCase,
} from './casos-de-uso';
import { RequisicoesController } from './requisicoes.controller';
import { RequisicoesService } from './requisicoes.service';
import { TransicaoRequisicaoService } from './transicao-requisicao.service';

@Module({
  imports: [AuthModule],
  controllers: [RequisicoesController],
  providers: [
    RequisicoesService,
    TransicaoRequisicaoService,
    SubmeterRequisicaoUseCase,
    ReenviarRequisicaoUseCase,
    AssumirTriagemUseCase,
    DevolverAjusteUseCase,
  ],
  exports: [RequisicoesService, TransicaoRequisicaoService],
})
export class RequisicoesModule {}
