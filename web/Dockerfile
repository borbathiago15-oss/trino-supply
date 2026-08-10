# Imagem do frontend (Next.js 15). Multi-stage: build e runtime em node:22-alpine.
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM node:22-alpine
WORKDIR /app
ENV NODE_ENV=production
COPY --from=build /app ./
ENV PORT=3000
EXPOSE 3000
CMD ["npm", "start"]
