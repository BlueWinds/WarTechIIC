#!/bin/bash
set -o errexit

export BTPATH=../BATTLETECH/game

RED='\033[0;31m'
NC='\033[0m' # No Color

if ! command -v nodemon &> /dev/null
then
    echo -e "${RED}nodemon could not be found, compiling once and exiting.${NC}"
    msbuild
    exit
fi

nodemon -x msbuild -w src/ -e .
