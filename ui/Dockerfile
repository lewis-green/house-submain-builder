# Built from the ui/ directory: docker build ui/
FROM node:22-alpine AS build
WORKDIR /src

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:1.27-alpine
# jq writes the runtime config, so a URL containing a character that matters to
# JSON cannot produce a file the app fails to parse.
RUN apk add --no-cache jq
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY docker-entrypoint.d/ /docker-entrypoint.d/
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 8080
