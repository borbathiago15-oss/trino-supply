import { Module } from '@nestjs/common';
import { AuthModule } from '../auth/auth.module';
import { FilaImportacao } from './fila';
import { ImportacaoController } from './importacao.controller';
import { ImportacaoStagingService } from './importacao-staging.service';
import { ProcessadorDeImportacaoWorker } from './processador.worker';

@Module({
  imports: [AuthModule],
  controllers: [ImportacaoController],
  providers: [FilaImportacao, ImportacaoStagingService, ProcessadorDeImportacaoWorker],
  exports: [ImportacaoStagingService, FilaImportacao, ProcessadorDeImportacaoWorker],
})
export class ImportacaoModule {}
