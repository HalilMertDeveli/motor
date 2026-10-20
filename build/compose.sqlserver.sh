cd ..
docker compose -f docker-compose.yml -f docker-compose.sqlserver.yml up -d --build
echo 'Press enter to exit...'; read dummy;