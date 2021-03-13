#!/bin/bash
set -o errexit

export BTPATH=../BATTLETECH/game
dir=$(pwd)

RED='\033[0;31m'
NC='\033[0m' # No Color

if ! command -v nodemon &> /dev/null
then
    echo -e "${RED}nodemon could not be found, compiling once and exiting.${NC}"
    msbuild
    exit
fi

nodemon -x "msbuild && cd '$BTPATH/Mods/WarTechIIC' && rm -f 'WarTechIIC.log' && rm -f WIIC_systemControl.json && zip -rq '$PWD/WarTechIIC.zip' ." -w src/ -e .
