VERSION=$ATLAS_VER$ATLAS_RELEASE

# Atlas first: Email and Observability depend on it at the same version
bash ./pack-proj.sh "Atlas" $VERSION "BrutalSystems.Atlas"
bash ./pack-proj.sh "Atlas.Email" $VERSION "BrutalSystems.Atlas.Email"
bash ./pack-proj.sh "Atlas.Observability" $VERSION "BrutalSystems.Atlas.Observability"
