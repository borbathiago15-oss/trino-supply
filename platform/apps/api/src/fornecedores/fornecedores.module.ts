import { Module } from '@nestjs/common';
import { AuthModule } from '../auth/auth.module';
import { ElegibilidadeService } from './elegibilidade.service';
import { FornecedoresController } from './fornecedores.controller';
import { FornecedoresService } from './fornecedores.service';

@Module({
  imports: [AuthModule],
  controllers: [FornecedoresController],
  providers: [FornecedoresService, ElegibilidadeService],
  // Exportado: qualquer módulo de compras futuro pergunta a elegibilidade aqui.
  exports: [ElegibilidadeService, FornecedoresService],
})
export class FornecedoresModule {}
