import { Module } from '@nestjs/common';
import { AuthModule } from '../auth/auth.module';
import { AprovacoesController } from './aprovacoes.controller';
import { AprovacoesService } from './aprovacoes.service';
import { AprovarEtapaUseCase } from './aprovar-etapa.usecase';

@Module({
  imports: [AuthModule],
  controllers: [AprovacoesController],
  providers: [AprovacoesService, AprovarEtapaUseCase],
  exports: [AprovacoesService, AprovarEtapaUseCase],
})
export class AprovacoesModule {}
