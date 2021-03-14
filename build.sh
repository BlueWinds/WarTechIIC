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

nodemon -x "msbuild && rm -f 'WarTechIIC.zip' && cd '$BTPATH/Mods/WarTechIIC' && zip -x WarTechIIC.log -x WIIC_systemControl.json -rq '$PWD/WarTechIIC.zip' ." -w src/ -e .
