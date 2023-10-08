# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

#!/bin/bash

DOCKER_SQL_PASS=$1
CERT_DIR=~/container/customerdb

# Create directory to store certificate
mkdir -p $CERT_DIR

# Create self-signed certificate.
openssl req -x509 -nodes -newkey rsa:2048 -subj '/CN=127.0.0.1' -keyout $CERT_DIR/mssql.key -out $CERT_DIR/mssql.pem -days 365
echo "Self-signed certificate created successfully."

# Assign read permissions to all.
chmod 555 $CERT_DIR/mssql.pem
chmod 555 $CERT_DIR/mssql.key
echo "Permissions modified successfully."

# Create mssql.conf file with the desired tlscert properties.
echo "[network]" >> $CERT_DIR/mssql.conf
echo "tlscert = /var/opt/mssql/mssql.pem" >> $CERT_DIR/mssql.conf
echo "tlskey = /var/opt/mssql/mssql.key" >> $CERT_DIR/mssql.conf
echo "tlsprotocols = 1.2" >> $CERT_DIR/mssql.conf
echo "forceencryption = 1" >> $CERT_DIR/mssql.conf
cat $CERT_DIR/mssql.conf

# Start mssql-server by volume mounting the cert, key and conf files.
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=$DOCKER_SQL_PASS" -p 1433:1433 --name customerdb -h customerdb -v $CERT_DIR/mssql.conf:/var/opt/mssql/mssql.conf -v $CERT_DIR/mssql.pem:/var/opt/mssql/mssql.pem -v $CERT_DIR/mssql.key:/var/opt/mssql/mssql.key -d mcr.microsoft.com/mssql/server:2019-latest
sleep 30
docker logs customerdb

# Install certificate as trusted for the client connection to succeed.
mv $CERT_DIR/mssql.pem $CERT_DIR/mssql.crt
sudo cp $CERT_DIR/mssql.crt /usr/local/share/ca-certificates
sudo update-ca-certificates --fresh
