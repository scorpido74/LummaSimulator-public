# LummaSimulator demo platform — one image, six services
FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

ARG SERVICE=portal
ENV SERVICE=${SERVICE} PYTHONUNBUFFERED=1
EXPOSE 8000

# Single in-container port (8000); host port mapping differs per service.
CMD ["sh", "-c", "python -m uvicorn ${SERVICE}.app:app --host 0.0.0.0 --port 8000 --log-level warning"]
