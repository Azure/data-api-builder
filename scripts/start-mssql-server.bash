#!/bin/bash

DockerSQLPass=$0

# Create self-signed certificate.
openssl req -x509 -nodes -newkey rsa:2048 -subj '/CN=localhost' -keyout /container/customerdb/mssql.key -out /container/customerdb/mssql.pem -days 365

# Assign read permissions to all.
chmod 444 /container/customerdb/mssql.pem
chmod 444 /container/customerdb/mssql.key

# Create mssql.conf file with the desired tlscert properties.
echo "[network]" >> /container/customerdb/mssql.conf
echo "tlscert = /var/opt/mssql/mssql.pem" >> /container/customerdb/mssql.conf
echo "tlskey = /var/opt/mssql/mssql.key" >> /container/customerdb/mssql.conf
echo "tlsprotocols = 1.2" >> /container/customerdb/mssql.conf
echo "forceencryption = 1" >> /container/customerdb/mssql.conf

# Install certificate as trusted for the client connection to succeed.
mv /container/customerdb/mssql.pem /container/customerdb/mssql.crt
cp /container/customerdb/mssql.crt /usr/local/share/ca-certificates

# Start mssql-server by volume mounting the cert, key and conf files.
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=$DockerSQLPass" -p 1433:1433 --name customerdb -h customerdb -v /container/customerdb/mssql.conf:/var/opt/mssql/mssql.conf -v /container/customerdb/mssql.pem:/var/opt/mssql/mssql.pem -v /container/customerdb/mssql.key:/var/opt/mssql/mssql.key -d mcr.microsoft.com/mssql/server:2019-latest
