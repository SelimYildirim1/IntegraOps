FROM node:24-alpine AS build
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build
RUN npm prune --omit=dev

FROM node:24-alpine
ARG APP_VERSION=dev
ARG APP_REVISION=unknown
ENV APP_VERSION=$APP_VERSION APP_REVISION=$APP_REVISION
LABEL org.opencontainers.image.source="https://github.com/SelimYildirim1/IntegraOps" \
      org.opencontainers.image.version="$APP_VERSION" \
      org.opencontainers.image.revision="$APP_REVISION" \
      org.opencontainers.image.title="integraops-panel"
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1
COPY --from=build --chown=node:node /app .
USER node
EXPOSE 3000
CMD ["node", "node_modules/next/dist/bin/next", "start", "--hostname", "0.0.0.0"]
