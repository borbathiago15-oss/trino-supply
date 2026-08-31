import { Global, Module } from '@nestjs/common';
import { AuthModule } from '../auth/auth.module';
import { SlaController } from './sla.controller';
import { SlaCalculatorService } from './sla.service';

/** Global: o executor de transições da requisição depende do calculador. */
@Global()
@Module({
  imports: [AuthModule],
  controllers: [SlaController],
  providers: [SlaCalculatorService],
  exports: [SlaCalculatorService],
})
export class SlaModule {}
