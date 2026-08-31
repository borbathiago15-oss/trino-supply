import 'reflect-metadata';
import * as dotenv from 'dotenv';
dotenv.config();

import { NestFactory } from '@nestjs/core';
import { AppModule } from './app.module';

async function bootstrap() {
  if (!process.env.JWT_SECRET) {
    // Falha fechado: sem segredo não há como assinar/validar tokens com segurança.
    throw new Error('JWT_SECRET é obrigatório (defina no ambiente ou no .env).');
  }
  const app = await NestFactory.create(AppModule);
  app.setGlobalPrefix('api/v1');
  const porta = Number(process.env.PORT ?? 3001);
  await app.listen(porta);
  console.log(`Trino Platform API ouvindo em http://127.0.0.1:${porta}/api/v1`);
}

bootstrap();
