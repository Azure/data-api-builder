#!/bin/bash
databaseType=$1;
commandsFileName="${databaseType}Commands.txt";
absolutePath=$(pwd -P);
commandsFileNameWithPath="$absolutePath/$commandsFileName";

#Fetching the path to dab dll
pathToDLL=$(find ./src/out/cli -name dab.dll)

#Generating the config using dab commands
echo "Generating config file using dab commands";
while read -r command; do
  cmd="dotnet ${pathToDLL} ${command}";
  eval $cmd;
done <$commandsFileNameWithPath;
