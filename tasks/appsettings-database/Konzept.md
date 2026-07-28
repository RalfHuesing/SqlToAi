src\SqlToAi\appsettings.json


aktuell haben wir:
    "Databases": {
      "Allowed": [
        "DemoDB"
      ],
      "Blocked": [
        "master",
        "msdb",
        "tempdb",
        "model"
      ],
      "CacheTtlSeconds": 300,
      "AccessCheckSql": "SELECT CASE WHEN DB_NAME() = 'DemoDB' AND SYSTEM_USER = 'Agent' THEN 'ReadWrite' ELSE 'None' END AS AccessLevel"
    },


these:
blocked raus -> alles was nicht erlaubt ist muss automatisch geblockt sein, konfig überflüssig

Allowed und AccessCheckSql ist anstrengend.
folgende änderung:
wir haben für die jeweiligen access levels einträge wo wir die datenbanken eintragen.
das wir das nicht mehr super flexibel mit sql machen können ist okay.
das kann eh keiner warten und die flexibilität braucht es nicht.