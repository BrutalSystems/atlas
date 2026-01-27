# $1 {project}
# $2 {version}
# $3 {package name}

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
SLNPATH=$DIR/..
PROJPATH=$SLNPATH/$1
CSPROJ=$PROJPATH/$1.csproj
NUPKGPATH=$PROJPATH/bin/Debug/$3.$2.nupkg

echo ========
echo Packing...  $PROJPATH
echo ========

dotnet pack --configuration Debug -p:Version=$2 $CSPROJ

dotnet nuget push "$NUPKGPATH" -s https://api.nuget.org/v3/index.json -k $NUGET_TOKEN