/*
 * selftest.c -- built-in regression selftest
 *
 * For each PLA file in examples_dir (iterated in sorted order) espresso
 * is run once, then its output is rendered in every supported output format
 * and each rendering is hashed with SHA-256.
 *
 * Expected hashes are stored in <examples_dir>/hash.txt, one line per
 * file in the format:
 *
 *   <basename>|<format>  <sha256hex>
 *
 * Use "espresso -selftest generate [dir]" to produce hash.txt from the
 * current outputs, then commit it alongside the test inputs.
 */

#include "selftest.h"
#include "espresso.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <dirent.h>
#  include <sys/stat.h>
#endif

/* Name of the hash file stored inside the examples directory. */
#define HASH_FILE_NAME "hash.txt"

/* =========================================================
 * Minimal SHA-256 (FIPS 180-4)
 * ========================================================= */

#define SHA_ROTR(x,n) (((uint32_t)(x) >> (n)) | ((uint32_t)(x) << (32-(n))))
#define SHA_CH(x,y,z)   (((x)&(y)) ^ (~(x)&(z)))
#define SHA_MAJ(x,y,z)  (((x)&(y)) ^ ((x)&(z)) ^ ((y)&(z)))
#define SHA_S0(x) (SHA_ROTR(x, 2) ^ SHA_ROTR(x,13) ^ SHA_ROTR(x,22))
#define SHA_S1(x) (SHA_ROTR(x, 6) ^ SHA_ROTR(x,11) ^ SHA_ROTR(x,25))
#define SHA_s0(x) (SHA_ROTR(x, 7) ^ SHA_ROTR(x,18) ^ ((uint32_t)(x) >>  3))
#define SHA_s1(x) (SHA_ROTR(x,17) ^ SHA_ROTR(x,19) ^ ((uint32_t)(x) >> 10))

static const uint32_t sha256_K[64] = {
    0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u,
    0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
    0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u,
    0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
    0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu,
    0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
    0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u,
    0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
    0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u,
    0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
    0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u,
    0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
    0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u,
    0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
    0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u,
    0x90befffa,  0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u
};

typedef struct {
    uint32_t state[8];
    uint8_t  buf[64];
    uint32_t buflen;
    uint64_t msglen;    /* bytes fed so far, saved before padding */
} sha256_ctx;

static void sha256_compress(sha256_ctx *ctx)
{
    uint32_t w[64], a, b, c, d, e, f, g, h, t1, t2;
    const uint8_t *p = ctx->buf;
    int i;

    for (i = 0; i < 16; i++)
        w[i] = ((uint32_t)p[4*i  ] << 24) | ((uint32_t)p[4*i+1] << 16)
             | ((uint32_t)p[4*i+2] <<  8) |  (uint32_t)p[4*i+3];
    for (i = 16; i < 64; i++)
        w[i] = SHA_s1(w[i-2]) + w[i-7] + SHA_s0(w[i-15]) + w[i-16];

    a=ctx->state[0]; b=ctx->state[1]; c=ctx->state[2]; d=ctx->state[3];
    e=ctx->state[4]; f=ctx->state[5]; g=ctx->state[6]; h=ctx->state[7];

    for (i = 0; i < 64; i++) {
        t1 = h + SHA_S1(e) + SHA_CH(e,f,g) + sha256_K[i] + w[i];
        t2 = SHA_S0(a) + SHA_MAJ(a,b,c);
        h=g; g=f; f=e; e=d+t1; d=c; c=b; b=a; a=t1+t2;
    }

    ctx->state[0]+=a; ctx->state[1]+=b; ctx->state[2]+=c; ctx->state[3]+=d;
    ctx->state[4]+=e; ctx->state[5]+=f; ctx->state[6]+=g; ctx->state[7]+=h;
}

static void sha256_init(sha256_ctx *ctx)
{
    ctx->state[0]=0x6a09e667u; ctx->state[1]=0xbb67ae85u;
    ctx->state[2]=0x3c6ef372u; ctx->state[3]=0xa54ff53au;
    ctx->state[4]=0x510e527fu; ctx->state[5]=0x9b05688cu;
    ctx->state[6]=0x1f83d9abu; ctx->state[7]=0x5be0cd19u;
    ctx->buflen = 0;
    ctx->msglen = 0;
}

static void sha256_update(sha256_ctx *ctx, const uint8_t *data, size_t len)
{
    size_t i;
    for (i = 0; i < len; i++) {
        ctx->buf[ctx->buflen++] = data[i];
        ctx->msglen++;
        if (ctx->buflen == 64) {
            sha256_compress(ctx);
            ctx->buflen = 0;
        }
    }
}

static void sha256_final(sha256_ctx *ctx, uint8_t digest[32])
{
    uint64_t bitlen = ctx->msglen * 8;
    uint8_t tmp[8];
    uint8_t pad = 0x80;
    int i;

    sha256_update(ctx, &pad, 1);
    pad = 0x00;
    while (ctx->buflen != 56)
        sha256_update(ctx, &pad, 1);

    tmp[0]=(uint8_t)(bitlen>>56); tmp[1]=(uint8_t)(bitlen>>48);
    tmp[2]=(uint8_t)(bitlen>>40); tmp[3]=(uint8_t)(bitlen>>32);
    tmp[4]=(uint8_t)(bitlen>>24); tmp[5]=(uint8_t)(bitlen>>16);
    tmp[6]=(uint8_t)(bitlen>> 8); tmp[7]=(uint8_t)(bitlen    );
    sha256_update(ctx, tmp, 8);

    for (i = 0; i < 8; i++) {
        digest[4*i  ]=(uint8_t)(ctx->state[i]>>24);
        digest[4*i+1]=(uint8_t)(ctx->state[i]>>16);
        digest[4*i+2]=(uint8_t)(ctx->state[i]>> 8);
        digest[4*i+3]=(uint8_t)(ctx->state[i]    );
    }
}

static void sha256_to_hex(const uint8_t digest[32], char out[65])
{
    static const char h[] = "0123456789abcdef";
    int i;
    for (i = 0; i < 32; i++) {
        out[2*i  ] = h[digest[i] >> 4];
        out[2*i+1] = h[digest[i] & 0xf];
    }
    out[64] = '\0';
}

/* =========================================================
 * File collection and sorting
 * ========================================================= */

typedef struct { char **names; int count, capacity; } file_list;

static void fl_add(file_list *fl, const char *path)
{
    if (fl->count == fl->capacity) {
        fl->capacity = fl->capacity ? fl->capacity * 2 : 32;
        fl->names = (char **)realloc(fl->names,
                                     (size_t)fl->capacity * sizeof(char *));
    }
    fl->names[fl->count++] = _strdup(path);
}

static int fl_cmp(const void *a, const void *b)
{
    return strcmp(*(const char * const *)a, *(const char * const *)b);
}

static void fl_free(file_list *fl)
{
    int i;
    for (i = 0; i < fl->count; i++) free(fl->names[i]);
    free(fl->names);
    fl->names = NULL; fl->count = fl->capacity = 0;
}

static void collect_files_r(const char *dir, file_list *fl)
{
#ifdef _WIN32
    WIN32_FIND_DATAA fd;
    HANDLE h;
    char pattern[MAX_PATH], path[MAX_PATH];
    _snprintf(pattern, sizeof(pattern), "%s/*", dir);
    h = FindFirstFileA(pattern, &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        if (strcmp(fd.cFileName, ".") == 0 || strcmp(fd.cFileName, "..") == 0)
            continue;
        _snprintf(path, sizeof(path), "%s/%s", dir, fd.cFileName);
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
            collect_files_r(path, fl);
        else
            fl_add(fl, path);
    } while (FindNextFileA(h, &fd));
    FindClose(h);
#else
    DIR *d = opendir(dir);
    struct dirent *entry;
    struct stat st;
    char path[4096];
    if (!d) return;
    while ((entry = readdir(d)) != NULL) {
        if (strcmp(entry->d_name, ".") == 0 || strcmp(entry->d_name, "..") == 0)
            continue;
        snprintf(path, sizeof(path), "%s/%s", dir, entry->d_name);
        if (stat(path, &st) == 0) {
            if (S_ISDIR(st.st_mode))
                collect_files_r(path, fl);
            else if (S_ISREG(st.st_mode))
                fl_add(fl, path);
        }
    }
    closedir(d);
#endif
}

static void collect_files(const char *dir, file_list *fl)
{
    collect_files_r(dir, fl);
    qsort(fl->names, (size_t)fl->count, sizeof(char *), fl_cmp);
}

/*
 * Build the hash.txt key for a file: the path relative to base_dir,
 * with forward slashes.  base_dir must not have a trailing slash.
 */
static void make_rel_key(const char *base_dir, const char *path,
                         char *out, size_t outsz)
{
    size_t base_len = strlen(base_dir);
    const char *p   = path;
    char       *o   = out;
    char       *end = out + outsz - 1;

    if (strncmp(p, base_dir, base_len) == 0) p += base_len;
    while (*p == '/' || *p == '\\') p++;
    while (*p && o < end)
        *o++ = (*p == '\\') ? '/' : *p, p++;
    *o = '\0';
}

/* =========================================================
 * Per-file espresso run
 * ========================================================= */

static void reset_espresso_globals(void)
{
    debug                 = 0;
    verbose_debug         = FALSE;
    echo_comments         = FALSE;
    echo_unknown_commands = TRUE;
    force_irredundant     = TRUE;
    skip_make_sparse      = FALSE;
    kiss                  = FALSE;
    pos                   = FALSE;
    print_solution        = TRUE;
    recompute_onset       = FALSE;
    remove_essential      = TRUE;
    single_expand         = FALSE;
    summary               = FALSE;
    trace                 = FALSE;
    unwrap_onset          = TRUE;
    use_random_order      = FALSE;
    use_super_gasp        = FALSE;
}

/*
 * Run espresso (default mode) on a single PLA file.
 * Returns the SHA-256 of its output as a hex string in file_hex[65].
 * Returns  0 on success,
 *         -1 on I/O error,
 *          1 if the cover-verify step detected an inconsistency.
 */
typedef int (*format_supported_fn)(pPLA PLA);

typedef struct {
    const char          *tag;
    int                  output_type;
    format_supported_fn  supported;
} output_format;

static int format_always_supported(pPLA PLA)
{
    return 1;
}

static int format_eqntott_supported(pPLA PLA)
{
    return cube.output != -1 && cube.num_mv_vars == 1;
}

static int cube_supports_kiss_output(pset_family A)
{
    register pset p, last;
    int var, i, part;

    foreach_set(A, last, p) {
        for(var = cube.num_binary_vars; var < cube.num_vars - 1; var++) {
            if (setp_implies(cube.var_mask[var], p))
                continue;
            part = -1;
            for(i = cube.first_part[var]; i <= cube.last_part[var]; i++) {
                if (is_in_set(p, i)) {
                    if (part != -1)
                        return 0;
                    part = i;
                }
            }
        }
    }
    return 1;
}

static int format_kiss_supported(pPLA PLA)
{
    return cube_supports_kiss_output(PLA->F) &&
           cube_supports_kiss_output(PLA->D);
}

static const output_format output_formats[] = {
    {"f",       F_type,                           format_always_supported},
    {"r",       R_type,                           format_always_supported},
    {"d",       D_type,                           format_always_supported},
    {"fd",      FD_type,                          format_always_supported},
    {"fr",      FR_type,                          format_always_supported},
    {"dr",      DR_type,                          format_always_supported},
    {"fdr",     FDR_type,                         format_always_supported},
    {"fc",      F_type  | CONSTRAINTS_type,       format_always_supported},
    {"rc",      R_type  | CONSTRAINTS_type,       format_always_supported},
    {"dc",      D_type  | CONSTRAINTS_type,       format_always_supported},
    {"fdc",     FD_type | CONSTRAINTS_type,       format_always_supported},
    {"frc",     FR_type | CONSTRAINTS_type,       format_always_supported},
    {"drc",     DR_type | CONSTRAINTS_type,       format_always_supported},
    {"fdrc",    FDR_type | CONSTRAINTS_type,      format_always_supported},
    {"cons",    CONSTRAINTS_type,                 format_always_supported},
    {"scons",   SYMBOLIC_CONSTRAINTS_type,        format_always_supported},
    {"pleasure",PLEASURE_type,                    format_always_supported},
    {"eqntott", EQNTOTT_type,                     format_eqntott_supported},
    {"kiss",    KISS_type,                        format_kiss_supported},
    {NULL,      0,                                NULL}
};

static int prepare_one_file(const char *path, pPLA *PLA_out, bool *verify_failed)
{
    FILE *fp;
    pPLA PLA;
    pcover Fold;
    bool error;
    cost_t cost;

    reset_espresso_globals();

    fp = fopen(path, "r");
    if (!fp) {
        fprintf(stderr, "selftest: cannot open %s\n", path);
        return -1;
    }
    PLA = NIL(PLA_t);
    if (read_pla(fp, TRUE, TRUE, FD_type, &PLA) == EOF) {
        fprintf(stderr, "selftest: cannot read PLA from %s\n", path);
        fclose(fp);
        return -1;
    }
    fclose(fp);
    PLA->filename = _strdup(path);
    filename = PLA->filename;

    Fold = sf_save(PLA->F);
    PLA->F = espresso(PLA->F, PLA->D, PLA->R);
    EXECUTE(error = verify(PLA->F, Fold, PLA->D), VERIFY_TIME, PLA->F, cost);
    if (error) {
        free_cover(PLA->F);
        PLA->F = Fold;
    } else {
        free_cover(Fold);
    }

    *PLA_out = PLA;
    *verify_failed = error;
    return 0;
}

static int hash_one_output(pPLA PLA, int output_type, char file_hex[65])
{
    FILE *tmp;
    sha256_ctx ctx;
    uint8_t digest[32], buf[4096];
    size_t n;

    tmp = tmpfile();
    if (!tmp) {
        fprintf(stderr, "selftest: tmpfile() failed\n");
        return -1;
    }
    fprint_pla(tmp, PLA, output_type);

    /* Hash the captured output. */
    rewind(tmp);
    sha256_init(&ctx);
    while ((n = fread(buf, 1, sizeof(buf), tmp)) > 0)
        sha256_update(&ctx, buf, n);
    sha256_final(&ctx, digest);
    sha256_to_hex(digest, file_hex);
    fclose(tmp);
    return 0;
}

static void cleanup_one_file(pPLA PLA)
{
    free_PLA(PLA);
    FREE(cube.part_size);
    setdown_cube();
}

/* =========================================================
 * Hash-file helpers
 * ========================================================= */

typedef struct { char name[600]; char hash[65]; } hash_entry;
typedef struct { hash_entry *entries; int count; }  hash_table;

static int ht_load(hash_table *ht, const char *path)
{
    FILE *f;
    char line[400];

    ht->entries = NULL;
    ht->count   = 0;
    f = fopen(path, "r");
    if (!f) return 0;

    while (fgets(line, sizeof(line), f)) {
        size_t len = strlen(line);
        char *sep, *hash;

        while (len > 0 && (line[len-1] == '\n' || line[len-1] == '\r'))
            line[--len] = '\0';
        if (len == 0) continue;

        sep = strchr(line, ' ');
        if (!sep) continue;
        *sep = '\0';
        hash = sep + 1;
        while (*hash == ' ') hash++;
        if (strlen(hash) != 64) continue;

        ht->entries = (hash_entry *)realloc(ht->entries,
                          (size_t)(ht->count + 1) * sizeof(hash_entry));
        strncpy(ht->entries[ht->count].name, line,
                sizeof(ht->entries[0].name) - 1);
        ht->entries[ht->count].name[sizeof(ht->entries[0].name) - 1] = '\0';
        memcpy(ht->entries[ht->count].hash, hash, 65);
        ht->count++;
    }
    fclose(f);
    return ht->count;
}

static const char *ht_lookup(const hash_table *ht, const char *name)
{
    int i;
    for (i = 0; i < ht->count; i++)
        if (strcmp(ht->entries[i].name, name) == 0)
            return ht->entries[i].hash;
    return NULL;
}

static void ht_free(hash_table *ht)
{
    free(ht->entries);
    ht->entries = NULL;
    ht->count   = 0;
}

static void make_format_key(const char *rel_key, const char *tag,
                            char *out, size_t outsz)
{
    _snprintf(out, outsz, "%s|%s", rel_key, tag);
    out[outsz - 1] = '\0';
}

static int is_hash_file_path(const char *path)
{
    const char *base1 = strrchr(path, '/');
    const char *base2 = strrchr(path, '\\');
    const char *base = base1;

    if (base2 != NULL && (base == NULL || base2 > base))
        base = base2;
    return strcmp(base ? base + 1 : path, HASH_FILE_NAME) == 0;
}

/* =========================================================
 * Public entry points
 * ========================================================= */

int run_selftest(const char *test_dir)
{
    file_list  fl;
    hash_table ht;
    char       hash_path[512], file_hex[65], rel_key[512], format_key[600];
    const char *dir;
    int        i, passed, failed;
    long       t_start;

    dir = (test_dir && *test_dir) ? test_dir : "tests";

    _snprintf(hash_path, sizeof(hash_path), "%s/" HASH_FILE_NAME, dir);
    memset(&ht, 0, sizeof(ht));
    if (!ht_load(&ht, hash_path)) {
        fprintf(stderr,
                "selftest: cannot load '%s'\n"
                "  Run: espresso -selftest generate %s\n",
                hash_path, dir);
        return 1;
    }

    memset(&fl, 0, sizeof(fl));
    collect_files(dir, &fl);

    if (fl.count == 0) {
        fprintf(stderr, "selftest: no files found in '%s'\n", dir);
        ht_free(&ht);
        return 1;
    }

    printf("Selftest: directory '%s'\n\n", dir);
    passed = failed = 0;
    t_start = ptime();

    for (i = 0; i < fl.count; i++) {
        const char *path = fl.names[i];
        pPLA        PLA;
        bool        verify_failed;
        int         rc;
        const output_format *fmt;

        make_rel_key(dir, path, rel_key, sizeof(rel_key));

        if (is_hash_file_path(path)) continue;

        rc = prepare_one_file(path, &PLA, &verify_failed);

        if (rc < 0) {
            printf("  [ERROR ] %s\n", rel_key);
            failed++;
        } else {
            for (fmt = output_formats; fmt->tag != NULL; fmt++) {
                const char *expected;

                if (! fmt->supported(PLA))
                    continue;

                make_format_key(rel_key, fmt->tag, format_key, sizeof(format_key));
                if (hash_one_output(PLA, fmt->output_type, file_hex) < 0) {
                    printf("  [ERROR ] %s\n", format_key);
                    failed++;
                    continue;
                }

                expected = ht_lookup(&ht, format_key);
                if (!expected) {
                    printf("  [UNKNWN] %-44s  %s  (not in %s)\n",
                           format_key, file_hex, HASH_FILE_NAME);
                    failed++;
                } else if (verify_failed) {
                    printf("  [VERIFY] %-44s  %s  (verify failed)\n",
                           format_key, file_hex);
                    failed++;
                } else if (strcmp(file_hex, expected) != 0) {
                    printf("  [HASH  ] %-44s  %s\n"
                           "           %-44s  %s  (expected)\n",
                           format_key, file_hex, "", expected);
                    failed++;
                } else {
                    printf("  [OK    ] %-44s  %s\n", format_key, file_hex);
                    passed++;
                }
            }
            cleanup_one_file(PLA);
        }
    }

    fl_free(&fl);
    ht_free(&ht);

    printf("\nResults : %d passed, %d failed\n", passed, failed);
    printf("Time    : %s\n\n", print_time(ptime() - t_start));

    return (failed > 0) ? 1 : 0;
}

int run_selftest_generate(const char *test_dir)
{
    file_list  fl;
    FILE      *out;
    char       hash_path[512], file_hex[65], rel_key[512], format_key[600];
    const char *dir;
    int        i, ok, errors;
    long       t_start;

    dir = (test_dir && *test_dir) ? test_dir : "tests";

    memset(&fl, 0, sizeof(fl));
    collect_files(dir, &fl);

    if (fl.count == 0) {
        fprintf(stderr, "selftest: no files found in '%s'\n", dir);
        return 1;
    }

    _snprintf(hash_path, sizeof(hash_path), "%s/" HASH_FILE_NAME, dir);
    out = fopen(hash_path, "w");
    if (!out) {
        fprintf(stderr, "selftest: cannot write '%s'\n", hash_path);
        fl_free(&fl);
        return 1;
    }

    printf("Generating hashes for files in '%s'...\n\n", dir);
    ok = errors = 0;
    t_start = ptime();

    for (i = 0; i < fl.count; i++) {
        const char *path = fl.names[i];
        pPLA        PLA;
        bool        verify_failed;
        int         rc;
        const output_format *fmt;

        make_rel_key(dir, path, rel_key, sizeof(rel_key));

        if (is_hash_file_path(path)) continue;

        rc = prepare_one_file(path, &PLA, &verify_failed);
        if (rc < 0) {
            printf("  [ERROR ] %s\n", rel_key);
            errors++;
        } else {
            for (fmt = output_formats; fmt->tag != NULL; fmt++) {
                if (! fmt->supported(PLA))
                    continue;
                make_format_key(rel_key, fmt->tag, format_key, sizeof(format_key));
                if (hash_one_output(PLA, fmt->output_type, file_hex) < 0) {
                    printf("  [ERROR ] %s\n", format_key);
                    errors++;
                } else {
                    fprintf(out, "%s  %s\n", format_key, file_hex);
                    printf("  [HASHED] %-44s  %s\n", format_key, file_hex);
                    ok++;
                }
            }
            cleanup_one_file(PLA);
        }
    }

    fclose(out);
    fl_free(&fl);

    printf("\nGenerated: %d hash(es), %d error(s)\n", ok, errors);
    printf("Hash file: %s\n", hash_path);
    printf("Time     : %s\n\n", print_time(ptime() - t_start));

    return (errors > 0) ? 1 : 0;
}
