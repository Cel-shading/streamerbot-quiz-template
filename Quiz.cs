// =====================================================================
//  QUIZ TWITCH POUR STREAMER.BOT
// ---------------------------------------------------------------------
//  Action : "Quiz"   |   Trigger : Twitch > Chat > Chat Message
//  Sub-action : Core > C# > Execute C# Code  (coller ce fichier entier)
//
//  Commandes (broadcaster + modérateurs) :
//    !quiz                    -> quiz sur une section tirée au hasard
//    !quiz 2                  -> quiz sur la section n°2
//    !quiz géographie         -> quiz sur la section "Géographie"
//    !quiz random             -> 5 questions au hasard, toutes sections
//    !quiz random 10          -> 10 questions au hasard, toutes sections
//    !quiz sections           -> liste des sections numérotées
//    !quiz skip               -> passe la question en cours
//    !quiz stop               -> arrête le quiz et affiche le classement
//
//  Les viewers répondent simplement en écrivant la réponse dans le tchat.
// =====================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

public class CPHInline
{
    // ============================ RÉGLAGES ============================

    // Fichier des questions. Chemin relatif = relatif au dossier de Streamer.bot.
    // Exemple de chemin absolu : @"C:\Streamer.bot\quiz\questions.txt"
    private static readonly string FICHIER_QUESTIONS = @"quiz\questions.txt";

    private static readonly int NB_QUESTIONS_RANDOM_DEFAUT = 5;  // !quiz random sans chiffre
    private static readonly int MAX_QUESTIONS_SECTION      = 0;  // !quiz / !quiz <section> : 0 = toute la section
    private static readonly bool MELANGER_QUESTIONS        = true;

    private static readonly int TEMPS_PAR_QUESTION_SEC     = 60; // 0 = pas de limite de temps
    private static readonly int DELAI_AVANT_DEBUT_SEC      = 5;  // entre l'annonce et la 1re question
    private static readonly int DELAI_ENTRE_QUESTIONS_SEC  = 5;  // entre une réponse et la question suivante

    private static readonly bool MODOS_PEUVENT_LANCER      = true;
    // Pseudos (login Twitch, en minuscules) autorisés en plus, au cas où. Ex : { "monpseudo", "monami" }
    private static readonly string[] ADMINS                = { };

    private static readonly bool TOLERER_FAUTES_DE_FRAPPE  = true; // "pariss" accepté pour "Paris"
    private static readonly bool IGNORER_ARTICLES          = true; // "Nil" accepté pour "Le Nil"
    private static readonly int TOP_CLASSEMENT             = 10;   // 0 = tout le monde

    // ============================ MESSAGES ============================
    // Variables : {section} {nb} {num} {total} {question} {user} {reponse} {score}

    private const string MSG_DEBUT          = "🎯 Quiz « {section} » : {nb} question(s) ! Répondez directement dans le tchat. C'est parti dans quelques secondes...";
    private const string MSG_QUESTION_MULTI = "❓ Question {num}/{total} : {question}";
    private const string MSG_QUESTION_SEULE = "❓ Question : {question}";
    private const string MSG_SECTION_RANDOM = " [{section}]"; // ajouté à la question en mode random
    private const string MSG_TROUVE         = "✅ @{user} a trouvé la bonne réponse : « {reponse} » ! ({score} bonne(s) réponse(s))";
    private const string MSG_TEMPS_ECOULE   = "⏰ Temps écoulé ! La réponse était « {reponse} ».";
    private const string MSG_PASSEE         = "⏭️ Question passée. La réponse était « {reponse} ».";
    private const string MSG_CLASSEMENT     = "🏆 Fin du quiz ! Classement (réponses trouvées) : ";
    private const string MSG_PERSONNE       = "🏁 Fin du quiz ! Personne n'a trouvé de réponse cette fois 😢";
    private const string MSG_ARRET          = "🛑 Quiz arrêté.";
    private const string MSG_DEJA_EN_COURS  = "⚠️ Un quiz est déjà en cours (« !quiz stop » pour l'arrêter).";
    private const string MSG_AUCUN_EN_COURS = "ℹ️ Aucun quiz en cours.";
    private const string MSG_SECTION_INCONNUE = "❌ Section « {section} » introuvable. Sections : ";
    private const string MSG_FICHIER_VIDE   = "❌ Aucune question trouvée dans le fichier : ";

    // ========================== FIN RÉGLAGES ==========================

    private class Question
    {
        public string Section;
        public string Texte;
        public List<string> Reponses = new List<string>();      // telles qu'écrites
        public List<string> ReponsesNorm = new List<string>();  // normalisées
    }

    private class Section
    {
        public string Nom;
        public List<Question> Questions = new List<Question>();
    }

    private class Joueur
    {
        public string Nom;
        public int Score;
        public long DernierPoint; // pour départager les égalités (le premier arrivé passe devant)
    }

    private enum Etat { Inactif, Annonce, Question, Pause }

    private static readonly Random Rng = new Random();
    private readonly object _verrou = new object();

    private Etat _etat = Etat.Inactif;
    private List<Question> _questions = new List<Question>();
    private int _index = -1;
    private bool _modeRandom;
    private readonly Dictionary<string, Joueur> _joueurs = new Dictionary<string, Joueur>();
    private long _compteurPoints;

    private Timer _timer;
    private int _jetonTimer;

    // ============================ POINT D'ENTRÉE ============================

    public bool Execute()
    {
        // On copie les arguments tout de suite (l'objet args est propre à cette exécution).
        string message  = Arg("message");
        if (message == null) message = Arg("rawInput");
        if (message == null) return true;
        message = message.Trim();
        if (message.Length == 0) return true;

        string userId   = Arg("userId");
        string login    = Arg("userName");
        string pseudo   = Arg("user");
        if (string.IsNullOrEmpty(pseudo)) pseudo = login;
        if (string.IsNullOrEmpty(pseudo)) return true;
        string cle = !string.IsNullOrEmpty(userId) ? userId : pseudo.ToLowerInvariant();

        string reste;
        if (EstCommandeQuiz(message, out reste))
        {
            if (PeutPiloter(userId, login, pseudo))
            {
                lock (_verrou) { TraiterCommande(reste); }
            }
            return true;
        }

        // Sinon : c'est peut-être une réponse.
        lock (_verrou) { TraiterReponse(cle, pseudo, message); }
        return true;
    }

    // Appelé par Streamer.bot quand le code est recompilé ou à la fermeture.
    public void Dispose()
    {
        lock (_verrou)
        {
            AnnulerTimer();
            _etat = Etat.Inactif;
        }
    }

    // ============================ COMMANDES ============================

    private static bool EstCommandeQuiz(string message, out string reste)
    {
        reste = "";
        if (!message.StartsWith("!quiz", StringComparison.OrdinalIgnoreCase)) return false;
        if (message.Length > 5 && !char.IsWhiteSpace(message[5])) return false; // ex. "!quizz"
        reste = message.Substring(5).Trim();
        return true;
    }

    private void TraiterCommande(string reste)
    {
        string[] mots = reste.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        string premier = mots.Length > 0 ? Normaliser(mots[0], false) : "";

        if (premier == "stop" || premier == "arret")
        {
            if (_etat == Etat.Inactif) { Envoyer(MSG_AUCUN_EN_COURS); return; }
            AnnulerTimer();
            Envoyer(MSG_ARRET);
            if (_index < 0) { _etat = Etat.Inactif; return; } // arrêté avant la 1re question
            Terminer();
            return;
        }

        if (premier == "skip" || premier == "passer" || premier == "next")
        {
            if (_etat != Etat.Question) { if (_etat == Etat.Inactif) Envoyer(MSG_AUCUN_EN_COURS); return; }
            AnnulerTimer();
            Envoyer(Remplir(MSG_PASSEE, "reponse", QuestionCourante().Reponses[0]));
            PreparerSuite();
            return;
        }

        List<Section> sections = ChargerSections();

        if (premier == "sections" || premier == "liste" || premier == "list")
        {
            if (sections.Count == 0) { Envoyer(MSG_FICHIER_VIDE + CheminFichier()); return; }
            EnvoyerLong("📚 Sections : ", ListeSections(sections), " | ");
            return;
        }

        if (_etat != Etat.Inactif) { Envoyer(MSG_DEJA_EN_COURS); return; }
        if (sections.Count == 0) { Envoyer(MSG_FICHIER_VIDE + CheminFichier()); return; }

        // ----- !quiz random [n] -----
        if (premier == "random" || premier == "aleatoire" || premier == "hasard")
        {
            int nb = NB_QUESTIONS_RANDOM_DEFAUT;
            int lu;
            if (mots.Length > 1 && int.TryParse(mots[1], out lu) && lu > 0) nb = lu;

            List<Question> toutes = sections.SelectMany(s => s.Questions).ToList();
            Melanger(toutes);
            Demarrer("Toutes sections", toutes.Take(nb).ToList(), true);
            return;
        }

        // ----- !quiz  /  !quiz <numéro>  /  !quiz <nom> -----
        Section choisie;
        if (mots.Length == 0)
        {
            choisie = sections[Rng.Next(sections.Count)];
        }
        else
        {
            choisie = TrouverSection(sections, reste);
            if (choisie == null)
            {
                EnvoyerLong(Remplir(MSG_SECTION_INCONNUE, "section", reste), ListeSections(sections), " | ");
                return;
            }
        }

        List<Question> liste = new List<Question>(choisie.Questions);
        if (MELANGER_QUESTIONS) Melanger(liste);
        if (MAX_QUESTIONS_SECTION > 0) liste = liste.Take(MAX_QUESTIONS_SECTION).ToList();
        Demarrer(choisie.Nom, liste, false);
    }

    private static Section TrouverSection(List<Section> sections, string saisie)
    {
        int num;
        if (int.TryParse(saisie.Trim(), out num))
            return (num >= 1 && num <= sections.Count) ? sections[num - 1] : null;

        string cible = Normaliser(saisie, false);
        if (cible.Length == 0) return null;

        // 1) nom exact  2) début du nom  3) contenu dans le nom  (seulement si un seul résultat)
        Section exacte = sections.FirstOrDefault(s => Normaliser(s.Nom, false) == cible);
        if (exacte != null) return exacte;

        List<Section> debut = sections.Where(s => Normaliser(s.Nom, false).StartsWith(cible)).ToList();
        if (debut.Count == 1) return debut[0];

        List<Section> contient = sections.Where(s => Normaliser(s.Nom, false).Contains(cible)).ToList();
        if (contient.Count == 1) return contient[0];

        return null;
    }

    private static List<string> ListeSections(List<Section> sections)
    {
        List<string> res = new List<string>();
        for (int i = 0; i < sections.Count; i++)
            res.Add((i + 1) + ". " + sections[i].Nom + " (" + sections[i].Questions.Count + ")");
        return res;
    }

    // ============================ DÉROULEMENT ============================

    private void Demarrer(string nomSection, List<Question> questions, bool modeRandom)
    {
        _questions = questions;
        _index = -1;
        _modeRandom = modeRandom;
        _joueurs.Clear();
        _compteurPoints = 0;
        _etat = Etat.Annonce;

        Envoyer(MSG_DEBUT.Replace("{section}", nomSection).Replace("{nb}", questions.Count.ToString()));
        Planifier(DELAI_AVANT_DEBUT_SEC, QuestionSuivante);
    }

    private void QuestionSuivante()
    {
        _index++;
        if (_index >= _questions.Count) { Terminer(); return; }

        Question q = QuestionCourante();
        string texte = _questions.Count > 1 ? MSG_QUESTION_MULTI : MSG_QUESTION_SEULE;
        texte = texte.Replace("{num}", (_index + 1).ToString())
                     .Replace("{total}", _questions.Count.ToString())
                     .Replace("{question}", q.Texte);
        if (_modeRandom) texte += MSG_SECTION_RANDOM.Replace("{section}", q.Section);

        _etat = Etat.Question;
        Envoyer(texte);

        if (TEMPS_PAR_QUESTION_SEC > 0)
            Planifier(TEMPS_PAR_QUESTION_SEC, TempsEcoule);
    }

    private void TempsEcoule()
    {
        if (_etat != Etat.Question) return;
        Envoyer(Remplir(MSG_TEMPS_ECOULE, "reponse", QuestionCourante().Reponses[0]));
        PreparerSuite();
    }

    private void TraiterReponse(string cle, string pseudo, string message)
    {
        if (_etat != Etat.Question) return;

        Question q = QuestionCourante();
        if (!EstBonneReponse(q, message)) return;

        AnnulerTimer();

        Joueur j;
        if (!_joueurs.TryGetValue(cle, out j))
        {
            j = new Joueur();
            _joueurs[cle] = j;
        }
        j.Nom = pseudo;
        j.Score++;
        j.DernierPoint = ++_compteurPoints;

        Envoyer(MSG_TROUVE.Replace("{user}", pseudo)
                          .Replace("{reponse}", q.Reponses[0])
                          .Replace("{score}", j.Score.ToString()));
        PreparerSuite();
    }

    private void PreparerSuite()
    {
        _etat = Etat.Pause;
        Planifier(_index + 1 >= _questions.Count ? 2 : DELAI_ENTRE_QUESTIONS_SEC, QuestionSuivante);
    }

    private void Terminer()
    {
        AnnulerTimer();
        _etat = Etat.Inactif;

        List<Joueur> classement = _joueurs.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DernierPoint)
            .ToList();

        if (classement.Count == 0) { Envoyer(MSG_PERSONNE); return; }

        List<string> lignes = new List<string>();
        int limite = TOP_CLASSEMENT > 0 ? Math.Min(TOP_CLASSEMENT, classement.Count) : classement.Count;
        for (int i = 0; i < limite; i++)
        {
            Joueur p = classement[i];
            int rang = 1 + classement.Count(x => x.Score > p.Score); // ex-aequo = même rang
            string medaille = rang == 1 ? "🥇" : rang == 2 ? "🥈" : rang == 3 ? "🥉" : rang + ".";
            lignes.Add(medaille + " " + p.Nom + " (" + p.Score + ")");
        }
        if (classement.Count > limite) lignes.Add("+" + (classement.Count - limite) + " autre(s)");

        EnvoyerLong(MSG_CLASSEMENT, lignes, " | ");
        _joueurs.Clear();
    }

    private Question QuestionCourante()
    {
        return _questions[_index];
    }

    // ============================ MINUTERIE ============================

    private void Planifier(int secondes, Action action)
    {
        AnnulerTimer();
        int jeton = _jetonTimer;
        _timer = new Timer(delegate
        {
            lock (_verrou)
            {
                if (jeton != _jetonTimer) return; // minuterie périmée
                try { action(); }
                catch (Exception ex) { CPH.LogError("[Quiz] " + ex); }
            }
        }, null, Math.Max(0, secondes) * 1000, Timeout.Infinite);
    }

    private void AnnulerTimer()
    {
        _jetonTimer++;
        if (_timer != null) { _timer.Dispose(); _timer = null; }
    }

    // ============================ RÉPONSES ============================

    private static bool EstBonneReponse(Question q, string message)
    {
        string saisie = Normaliser(message, IGNORER_ARTICLES);
        if (saisie.Length == 0) return false;

        foreach (string attendue in q.ReponsesNorm)
        {
            if (attendue.Length == 0) continue;
            if (saisie == attendue) return true;

            if (TOLERER_FAUTES_DE_FRAPPE && !attendue.Any(char.IsDigit))
            {
                int tolerance = attendue.Length >= 10 ? 2 : attendue.Length >= 5 ? 1 : 0;
                if (tolerance > 0 && Math.Abs(saisie.Length - attendue.Length) <= tolerance
                    && Levenshtein(saisie, attendue) <= tolerance)
                    return true;
            }
        }
        return false;
    }

    private static readonly HashSet<string> Articles = new HashSet<string>
    {
        "le", "la", "les", "l", "un", "une", "des", "du", "de", "d", "the", "a", "an"
    };

    // minuscules, sans accents, sans ponctuation, espaces simples (+ articles retirés en tête)
    private static string Normaliser(string s, bool sansArticles)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.ToLowerInvariant()
             .Replace("œ", "oe").Replace("æ", "ae").Replace("ß", "ss")
             .Replace("’", "'").Replace("&", " et ");

        string decompose = s.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder(decompose.Length);
        foreach (char c in decompose)
        {
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        List<string> mots = sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (sansArticles)
            while (mots.Count > 1 && Articles.Contains(mots[0])) mots.RemoveAt(0);
        return string.Join(" ", mots);
    }

    private static int Levenshtein(string a, string b)
    {
        int[] prec = new int[b.Length + 1];
        int[] cour = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prec[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cour[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cout = a[i - 1] == b[j - 1] ? 0 : 1;
                cour[j] = Math.Min(Math.Min(cour[j - 1] + 1, prec[j] + 1), prec[j - 1] + cout);
            }
            int[] tmp = prec; prec = cour; cour = tmp;
        }
        return prec[b.Length];
    }

    // ============================ FICHIER ============================
    //  [Nom de la section]
    //  Question ? | réponse | autre réponse acceptée
    //  # commentaire

    private static string CheminFichier()
    {
        return Path.IsPathRooted(FICHIER_QUESTIONS)
            ? FICHIER_QUESTIONS
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FICHIER_QUESTIONS);
    }

    private List<Section> ChargerSections()
    {
        List<Section> sections = new List<Section>();
        string chemin = CheminFichier();
        if (!File.Exists(chemin))
        {
            CPH.LogError("[Quiz] Fichier introuvable : " + chemin);
            return sections;
        }

        Section courante = null;
        int numLigne = 0;
        foreach (string brute in File.ReadAllLines(chemin, Encoding.UTF8))
        {
            numLigne++;
            string ligne = brute.Trim();
            if (ligne.Length == 0 || ligne.StartsWith("#") || ligne.StartsWith("//")) continue;

            if (ligne.StartsWith("[") && ligne.EndsWith("]"))
            {
                string nom = ligne.Substring(1, ligne.Length - 2).Trim();
                courante = sections.FirstOrDefault(s => s.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase));
                if (courante == null)
                {
                    courante = new Section { Nom = nom };
                    sections.Add(courante);
                }
                continue;
            }

            string[] morceaux = ligne.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            if (morceaux.Length < 2)
            {
                CPH.LogWarn("[Quiz] Ligne " + numLigne + " ignorée (format attendu : question | réponse) : " + ligne);
                continue;
            }

            if (courante == null)
            {
                courante = new Section { Nom = "Divers" };
                sections.Add(courante);
            }

            Question q = new Question { Section = courante.Nom, Texte = morceaux[0] };
            for (int i = 1; i < morceaux.Length; i++)
            {
                q.Reponses.Add(morceaux[i]);
                q.ReponsesNorm.Add(Normaliser(morceaux[i], IGNORER_ARTICLES));
            }
            courante.Questions.Add(q);
        }

        return sections.Where(s => s.Questions.Count > 0).ToList();
    }

    // ============================ UTILITAIRES ============================

    private bool PeutPiloter(string userId, string login, string pseudo)
    {
        string loginMin = (login ?? pseudo ?? "").ToLowerInvariant();
        if (ADMINS.Any(a => a.Trim().ToLowerInvariant() == loginMin)) return true;

        string idDiffuseur = Arg("broadcastUserId");
        if (!string.IsNullOrEmpty(userId) && userId == idDiffuseur) return true;

        string loginDiffuseur = Arg("broadcastUserName") ?? Arg("broadcastUser");
        if (!string.IsNullOrEmpty(loginDiffuseur) && loginDiffuseur.ToLowerInvariant() == loginMin) return true;

        if (ArgBool("isBroadcaster")) return true;

        int role;
        bool roleLu = int.TryParse(Arg("role") ?? "", out role);
        if (roleLu && role >= 4) return true;                         // 4 = broadcaster

        if (!MODOS_PEUVENT_LANCER) return false;
        return ArgBool("isModerator") || (roleLu && role == 3);       // 3 = modérateur
    }

    private string Arg(string nom)
    {
        object v;
        if (args != null && args.TryGetValue(nom, out v) && v != null) return v.ToString();
        return null;
    }

    private bool ArgBool(string nom)
    {
        bool b;
        return bool.TryParse(Arg(nom) ?? "", out b) && b;
    }

    private static string Remplir(string modele, string cle, string valeur)
    {
        return modele.Replace("{" + cle + "}", valeur);
    }

    private static void Melanger<T>(List<T> liste)
    {
        for (int i = liste.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            T tmp = liste[i]; liste[i] = liste[j]; liste[j] = tmp;
        }
    }

    private void Envoyer(string texte)
    {
        if (texte.Length > 490) texte = texte.Substring(0, 487) + "...";
        CPH.SendMessage(texte, true, true);
    }

    // Découpe une liste en plusieurs messages pour respecter la limite Twitch (500 caractères).
    private void EnvoyerLong(string entete, List<string> elements, string separateur)
    {
        StringBuilder sb = new StringBuilder(entete);
        bool vide = true;
        foreach (string e in elements)
        {
            if (!vide && sb.Length + separateur.Length + e.Length > 450)
            {
                Envoyer(sb.ToString());
                sb.Clear();
                vide = true;
            }
            if (!vide) sb.Append(separateur);
            sb.Append(e);
            vide = false;
        }
        if (sb.Length > 0) Envoyer(sb.ToString());
    }
}
