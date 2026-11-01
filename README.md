# Quiz Twitch pour Streamer.bot

Un quiz dans le tchat Twitch : Streamer.bot pose les questions, le premier viewer qui écrit la bonne réponse marque un point, puis le quiz passe à la question suivante. À la fin, le classement s'affiche.

Les messages du quiz sont postés avec **ton compte diffuseur** : pas besoin de compte bot séparé.

## Fichiers

| Fichier | Rôle |
|---|---|
| `Quiz.cs` | Le code C# à coller dans Streamer.bot |
| `quiz/questions.txt` | Les questions, rangées par sections (à modifier librement) |

## Installation (5 minutes)

1. **Copier les questions** : copie le dossier `quiz` dans le dossier de Streamer.bot (celui qui contient `Streamer.bot.exe`). Tu dois obtenir `...\Streamer.bot\quiz\questions.txt`.
2. **Créer l'action** : onglet **Actions** → clic droit → **Add** → nom : `Quiz`.
3. **Ajouter le déclencheur** : dans le panneau **Triggers** de l'action → clic droit → **Add** → **Twitch** → **Chat** → **Chat Message**.
4. **Ajouter le code** : dans le panneau **Sub-Actions** → clic droit → **Add** → **Core** → **C#** → **Execute C# Code**.
   Efface le code d'exemple, colle **tout** le contenu de `Quiz.cs`, clique sur **Compile** (tu dois voir *Compiled successfully*), puis sur **Save and Compile**.
5. *(Conseillé)* Dans les réglages de l'action, mets **Queue** sur une nouvelle file `Quiz` avec la case **Blocking** cochée : les messages du tchat sont alors traités un par un.

> ⚠️ Ne crée **pas** de commande `!quiz` dans l'onglet *Commands*. Le script lit lui-même le tchat. Une commande en plus ferait traiter chaque `!quiz` deux fois.

## Commandes

Réservées au streamer et aux modérateurs :

| Commande | Effet |
|---|---|
| `!quiz` | Quiz sur une section tirée au hasard |
| `!quiz 2` | Quiz sur la section n°2 |
| `!quiz géographie` | Quiz sur la section « Géographie » (`!quiz geo` marche aussi) |
| `!quiz random` | 5 questions au hasard, toutes sections confondues |
| `!quiz random 10` | 10 questions au hasard, toutes sections confondues |
| `!quiz sections` | Affiche la liste des sections avec leur numéro |
| `!quiz skip` | Passe la question en cours (le bot donne la réponse) |
| `!quiz stop` | Arrête le quiz et affiche le classement |

Les viewers n'ont aucune commande à taper : ils **écrivent juste la réponse** dans le tchat.

Tes propres messages (hors commandes `!quiz`) ne comptent pas comme réponses. Tu connais les réponses, et comme c'est ton compte qui poste les questions, le quiz ne doit jamais juger ses propres messages. Pour tester seul, voir [En cas de problème](#en-cas-de-problème).

### Exemple dans le tchat

Les messages automatiques apparaissent sous ton pseudo :

```
Toi          : !quiz random 3
Toi (auto)   : 🎯 Quiz « Toutes sections » : 3 question(s) ! Répondez directement dans le tchat...
Toi (auto)   : ❓ Question 1/3 : Quelle planète est surnommée la planète rouge ? [Sciences]
Alice        : jupiter
Bob          : MARS !
Toi (auto)   : ✅ @Bob a trouvé la bonne réponse : « Mars » ! (1 bonne(s) réponse(s))
Toi (auto)   : ❓ Question 2/3 : En quelle année est tombé le mur de Berlin ? [Histoire]
...
Toi (auto)   : 🏆 Fin du quiz ! Classement (réponses trouvées) : 🥇 Bob (2) | 🥈 Alice (1)
```

## Écrire ses questions

Ouvre `quiz/questions.txt` avec le Bloc-notes et enregistre-le en **UTF-8** (c'est le choix par défaut) :

```
[Nom de la section]
Question ? | Réponse
Question ? | Réponse affichée | Autre réponse acceptée | Encore une autre
# Une ligne qui commence par # est un commentaire
```

- Les sections sont numérotées dans l'ordre du fichier (1, 2, 3...).
- Le fichier est relu à chaque `!quiz`, pas besoin de redémarrer quoi que ce soit.
- La **première** réponse est celle que le bot affiche.
- Les réponses sont comparées sans tenir compte des majuscules, des accents, de la ponctuation et des articles en début de réponse (`le`, `la`, `l'`, `les`, `un`, `une`, `du`...). Ainsi « nil », « Le Nil » et « LE NIL !! » sont tous acceptés pour `Le Nil`.
- Une petite faute de frappe est tolérée pour les réponses de 5 lettres ou plus (« canbera » pour « Canberra »). Les réponses qui contiennent des chiffres doivent être exactes (« 1788 » ≠ « 1789 »).
- Une phrase qui *contient* la réponse ne compte pas (« c'est Paris » ≠ « Paris »). Les viewers ne peuvent donc pas gagner en tapant plusieurs propositions dans un seul message.
- Pour les nombres, ajoute aussi la version en lettres : `... | 4 | quatre`.

## Réglages

Tout est en haut de `Quiz.cs`, dans le bloc `RÉGLAGES`. Après une modification, refais **Compile** puis **Save and Compile** dans Streamer.bot.

| Réglage | Défaut | Description |
|---|---|---|
| `FICHIER_QUESTIONS` | `quiz\questions.txt` | Chemin du fichier (relatif au dossier de Streamer.bot, ou chemin complet) |
| `NB_QUESTIONS_RANDOM_DEFAUT` | `5` | Nombre de questions pour `!quiz random` sans chiffre |
| `MAX_QUESTIONS_SECTION` | `0` | Nombre max de questions pour `!quiz` et `!quiz <section>` (0 = toute la section) |
| `MELANGER_QUESTIONS` | `true` | Mélange l'ordre des questions d'une section |
| `TEMPS_PAR_QUESTION_SEC` | `60` | Temps avant que le bot donne la réponse (0 = illimité) |
| `DELAI_AVANT_DEBUT_SEC` | `5` | Pause entre l'annonce du quiz et la 1re question |
| `DELAI_ENTRE_QUESTIONS_SEC` | `5` | Pause entre une bonne réponse et la question suivante |
| `ENVOYER_AVEC_COMPTE_BOT` | `false` | `false` = messages postés avec ton compte diffuseur. `true` = avec un compte bot connecté dans Streamer.bot |
| `DIFFUSEUR_PEUT_REPONDRE` | `false` | `true` = tes messages comptent comme réponses (utile pour tester seul) |
| `MODOS_PEUVENT_LANCER` | `true` | Les modérateurs peuvent piloter le quiz |
| `ADMINS` | vide | Pseudos autorisés en plus, ex. `{ "monpseudo" }` |
| `TOLERER_FAUTES_DE_FRAPPE` | `true` | Accepte une petite faute de frappe |
| `IGNORER_ARTICLES` | `true` | Ignore le / la / l' / les... en début de réponse |
| `TOP_CLASSEMENT` | `10` | Nombre de viewers affichés dans le classement (0 = tous) |

Tous les textes du bot (`MSG_...`) sont modifiables. Garde les variables entre accolades, par exemple `{user}`, `{reponse}`, `{num}` ou `{total}`.

## En cas de problème

- **« Aucune question trouvée dans le fichier : ... »** : le message indique le chemin cherché. Vérifie que `questions.txt` s'y trouve, ou mets un chemin complet dans `FICHIER_QUESTIONS`.
- **`!quiz` ne fait rien** : vérifie que le déclencheur est bien *Twitch → Chat → Chat Message* et que l'action est activée. Si Streamer.bot ne te reconnaît pas comme streamer, ajoute ton pseudo dans `ADMINS`.
- **Je teste seul et mes réponses ne comptent pas** : c'est normal. Mets `DIFFUSEUR_PEUT_REPONDRE = true` le temps du test (puis remets `false`), ou réponds avec un second compte Twitch.
- **Erreurs** : regarde les logs de Streamer.bot (onglet *Logs*). Les messages du script commencent par `[Quiz]`.
