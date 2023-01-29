#!/bin/bash

# Exit on error.
set -e

DOCKER_SQL_PASS=$0
CERT_DIR=~/container/customerdb

# Create directory to store certificate
mkdir -p $CERT_DIR

# Create self-signed certificate.
openssl req -x509 -nodes -newkey rsa:2048 -subj '/CN=localhost' -keyout $CERT_DIR/mssql.key -out $CERT_DIR/mssql.pem -days 365

# Assign read permissions to all.
chmod 444 $CERT_DIR/mssql.pem
chmod 444 $CERT_DIR/mssql.key

# Create mssql.conf file with the desired tlscert properties.
echo "[network]" >> $CERT_DIR/mssql.conf
echo "tlscert = /var/opt/mssql/mssql.pem" >> $CERT_DIR/mssql.conf
echo "tlskey = /var/opt/mssql/mssql.key" >> $CERT_DIR/mssql.conf
echo "tlsprotocols = 1.2" >> $CERT_DIR/mssql.conf
echo "forceencryption = 1" >> $CERT_DIR/mssql.conf

# Install certificate as trusted for the client connection to succeed.
mv $CERT_DIR/mssql.pem $CERT_DIR/mssql.crt
sudo cp $CERT_DIR/mssql.crt /usr/local/share/ca-certificates
sudo update-ca-certificates --fresh

# Start mssql-server by volume mounting the cert, key and conf files.
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=$DOCKER_SQL_PASS" -p 1433:1433 --name customerdb -h customerdb -v $CERT_DIR/mssql.conf:/var/opt/mssql/mssql.conf -v $CERT_DIR/mssql.pem:/var/opt/mssql/mssql.pem -v $CERT_DIR/mssql.key:/var/opt/mssql/mssql.key -d mcr.microsoft.com/mssql/server:2019-latest
