cd ..
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d --build
echo 'Press enter to exit...'; read dummy;