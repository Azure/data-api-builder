#!/bin/bash
databaseType=$1;
fileName="${databaseType}Commands.txt";
#Fetching the path to dab dll
pathToDLL=$(find ./src/out/cli -name dab.dll)
#Generating the config using dab commands
echo "Generating config file using dab commands";
while read -r command; do
  cmd="dotnet ${pathToDLL} ${command}";
  eval $cmd;
done <$fileName;
echo "Successfully generated the config file."
